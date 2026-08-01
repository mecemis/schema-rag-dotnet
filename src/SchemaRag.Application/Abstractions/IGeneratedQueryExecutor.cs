using SchemaRag.Application.Sql;

namespace SchemaRag.Application.Abstractions;

public interface IGeneratedQueryExecutor
{
    Task<QueryExecutionResult> ExecuteAsync(
        string dataSourceId,
        ValidatedSql sql,
        CancellationToken cancellationToken);
}

public sealed record QueryExecutionResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long ExecutionTimeMs);