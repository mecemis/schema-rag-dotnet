namespace SchemaRag.Application.Sql;

public sealed class UnsafeSqlException : Exception
{
    public UnsafeSqlException(string message)
        : base(message)
    {
    }
}