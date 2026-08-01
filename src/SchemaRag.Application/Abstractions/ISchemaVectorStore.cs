using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Abstractions;

public interface ISchemaVectorStore
{
    Task<IReadOnlyList<SchemaDocument>> SearchAsync(
        string dataSourceId,
        IReadOnlyCollection<string> allowedTableIds,
        float[] embedding,
        int limit,
        CancellationToken cancellationToken);

    Task ReplaceAsync(
        string dataSourceId,
        IReadOnlyList<SchemaVector> schemaVectors,
        CancellationToken cancellationToken);
}

public sealed record SchemaVector(
    SchemaDocument Document,
    float[] Embedding,
    string SchemaVersion);