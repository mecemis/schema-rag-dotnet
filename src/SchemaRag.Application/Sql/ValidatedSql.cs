namespace SchemaRag.Application.Sql;

public sealed record ValidatedSql
{
    internal ValidatedSql(string value)
    {
        Value = value;
    }

    public string Value { get; }
}