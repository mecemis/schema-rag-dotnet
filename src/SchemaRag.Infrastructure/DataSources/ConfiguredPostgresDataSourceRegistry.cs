using Microsoft.Extensions.Options;

using Npgsql;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.DataSources;
using SchemaRag.Domain.DataSources;
using SchemaRag.Infrastructure.Configuration;

namespace SchemaRag.Infrastructure.DataSources;

public sealed class ConfiguredPostgresDataSourceRegistry : IDataSourceRegistry, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, ConfiguredSource> _sources;

    public ConfiguredPostgresDataSourceRegistry(
        IOptions<DataSourcesOptions> dataSourcesOptions,
        IOptions<DatabaseOptions> databaseOptions)
    {
        var options = dataSourcesOptions.Value;
        DefaultDataSourceId = options.DefaultId;
        _sources = options.Profiles.ToDictionary(
            profile => profile.Key,
            profile => CreateSource(
                profile.Key,
                profile.Value,
                options.DefaultId,
                databaseOptions.Value.CommandTimeoutSeconds),
            StringComparer.Ordinal);
    }

    public string DefaultDataSourceId { get; }

    public IReadOnlyList<DataSourceDescriptor> GetAll() =>
        _sources.Values
            .Select(source => source.Descriptor)
            .OrderByDescending(source => source.IsDefault)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public DataSourceDescriptor GetRequired(string dataSourceId) =>
        GetRequiredSource(dataSourceId).Descriptor;

    public IReadOnlyList<string> GetIncludedSchemas(string dataSourceId) =>
        GetRequiredSource(dataSourceId).IncludedSchemas;

    public async Task<NpgsqlConnection> OpenReadOnlyConnectionAsync(
        string dataSourceId,
        CancellationToken cancellationToken)
    {
        var source = GetRequiredSource(dataSourceId);
        var connection = await source.DataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand("SHOW transaction_read_only", connection);
            var readOnlySetting = await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(readOnlySetting?.ToString(), "on", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Data source '{dataSourceId}' must use a PostgreSQL role with default_transaction_read_only=on.");
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources.Values)
        {
            await source.DataSource.DisposeAsync();
        }
    }

    private ConfiguredSource GetRequiredSource(string dataSourceId)
    {
        if (string.IsNullOrWhiteSpace(dataSourceId) || !_sources.TryGetValue(dataSourceId, out var source))
        {
            throw new DataSourceNotFoundException(dataSourceId ?? string.Empty);
        }

        return source;
    }

    private static ConfiguredSource CreateSource(
        string id,
        PostgresDataSourceOptions profile,
        string defaultId,
        int commandTimeoutSeconds)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(profile.ReadOnlyConnectionString)
        {
            ApplicationName = $"SchemaRag.ReadOnly.{id}",
            CommandTimeout = commandTimeoutSeconds
        };
        var dataSource = NpgsqlDataSource.Create(connectionString.ConnectionString);
        var descriptor = new DataSourceDescriptor(
            id,
            profile.DisplayName,
            string.Equals(id, defaultId, StringComparison.Ordinal));
        var schemas = profile.IncludedSchemas
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new ConfiguredSource(descriptor, schemas, dataSource);
    }

    private sealed record ConfiguredSource(
        DataSourceDescriptor Descriptor,
        IReadOnlyList<string> IncludedSchemas,
        NpgsqlDataSource DataSource);
}