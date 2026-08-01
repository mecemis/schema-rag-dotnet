using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Options;

using Npgsql;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Sql;
using SchemaRag.Infrastructure.Configuration;
using SchemaRag.Infrastructure.DataSources;

namespace SchemaRag.Infrastructure.QueryExecution;

public sealed class NpgsqlGeneratedQueryExecutor(
    ConfiguredPostgresDataSourceRegistry dataSourceRegistry,
    IOptions<DatabaseOptions> options) : IGeneratedQueryExecutor
{
    public async Task<QueryExecutionResult> ExecuteAsync(
        string dataSourceId,
        ValidatedSql sql,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(sql);
        var startedAt = Stopwatch.GetTimestamp();
        var settings = options.Value;

        await using var connection = await dataSourceRegistry.OpenReadOnlyConnectionAsync(
            dataSourceId,
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ConfigureReadOnlyTransactionAsync(
            connection,
            transaction,
            settings.CommandTimeoutSeconds,
            cancellationToken);

        await using var command = new NpgsqlCommand(sql.Value, connection, transaction)
        {
            CommandTimeout = settings.CommandTimeoutSeconds
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = CreateUniqueColumnNames(reader);
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (rows.Count < settings.MaxRows && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
            for (var index = 0; index < columns.Count; index++)
            {
                row[columns[index]] = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(new ReadOnlyDictionary<string, object?>(row));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        return new QueryExecutionResult(
            columns,
            rows,
            (long)elapsed.TotalMilliseconds);
    }

    private static async Task ConfigureReadOnlyTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using (var readOnlyCommand = new NpgsqlCommand(
                         "SET TRANSACTION READ ONLY",
                         connection,
                         transaction))
        {
            await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var timeoutCommand = new NpgsqlCommand(
            "SELECT set_config('statement_timeout', @timeout, true)",
            connection,
            transaction);
        timeoutCommand.Parameters.AddWithValue(
            "timeout",
            $"{(commandTimeoutSeconds * 1000).ToString(CultureInfo.InvariantCulture)}ms");
        await timeoutCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> CreateUniqueColumnNames(NpgsqlDataReader reader)
    {
        var names = new List<string>(reader.FieldCount);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < reader.FieldCount; index++)
        {
            var name = reader.GetName(index);
            counts.TryGetValue(name, out var count);
            var nextCount = count + 1;
            counts[name] = nextCount;
            names.Add(nextCount == 1 ? name : $"{name}_{nextCount}");
        }

        return names;
    }
}