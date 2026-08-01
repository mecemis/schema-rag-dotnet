namespace SchemaRag.Infrastructure.Configuration;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string BaseUrl { get; init; } = "http://localhost:6333";
    public string CollectionName { get; init; } = "schema_documents";
    public int RequestTimeoutSeconds { get; init; } = 30;
}