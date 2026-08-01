namespace SchemaRag.Domain.Schema;

public sealed record SchemaDocument(
    string DataSourceId,
    string SchemaName,
    string TableName,
    string BusinessDescription,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<SchemaRelationship> Relationships)
{
    public string TableId => SchemaTableIdentity.CreateId(
        DataSourceId,
        SchemaName,
        TableName);

    public string QualifiedTableName => $"{SchemaName}.{TableName}";
}