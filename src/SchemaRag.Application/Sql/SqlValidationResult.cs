namespace SchemaRag.Application.Sql;

public sealed record SqlValidationResult
{
    private SqlValidationResult(ValidatedSql? sql, string? error)
    {
        Sql = sql;
        Error = error;
    }

    public bool IsValid => Sql is not null;
    public ValidatedSql? Sql { get; }
    public string? Error { get; }

    internal static SqlValidationResult Success(string sql) =>
        new(new ValidatedSql(sql), null);

    internal static SqlValidationResult Failure(string error) =>
        new(null, error);
}