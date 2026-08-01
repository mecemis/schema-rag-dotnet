using System.Text;

using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Indexing;

public static class SchemaTextFormatter
{
    public static string Format(SchemaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();
        text.AppendLine($"Table: {document.QualifiedTableName}");
        text.AppendLine($"Business description: {document.BusinessDescription}");
        text.AppendLine("Columns:");

        foreach (var column in document.Columns)
        {
            var key = column.IsPrimaryKey ? ", primary key" : string.Empty;
            text.AppendLine($"- {column.Name} ({column.DataType}{key}): {column.Description}");
        }

        text.AppendLine("Relationships:");
        foreach (var relationship in document.Relationships)
        {
            text.AppendLine(
                $"- {document.QualifiedTableName}.{relationship.Column} -> " +
                $"{relationship.QualifiedReferencedTable}.{relationship.ReferencedColumn}: " +
                relationship.Description);
        }

        return text.ToString();
    }
}