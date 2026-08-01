namespace SchemaRag.Domain.Schema;

public sealed record SchemaColumn(
    string Name,
    string DataType,
    string Description,
    bool IsNullable,
    bool IsPrimaryKey = false);