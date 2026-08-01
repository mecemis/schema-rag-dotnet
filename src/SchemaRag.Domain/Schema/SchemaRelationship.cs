namespace SchemaRag.Domain.Schema;

public sealed record SchemaRelationship(
    string Column,
    string ReferencedSchemaName,
    string ReferencedTable,
    string ReferencedColumn,
    string Description)
{
    public string QualifiedReferencedTable => $"{ReferencedSchemaName}.{ReferencedTable}";
}