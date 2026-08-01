namespace SchemaRag.Application.Queries;

public sealed record NaturalLanguageQueryResult(
    string DataSourceId,
    string Question,
    IReadOnlyList<string> RetrievedTables,
    string GeneratedSql,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long ExecutionTimeMs);