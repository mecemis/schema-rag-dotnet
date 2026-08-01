namespace SchemaRag.Api.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public int SchemaRetrievalCount { get; init; } = 4;
    public int MaxQuestionLength { get; init; } = 500;
    public int MaxTablesPerIndex { get; init; } = 100;
    public int EmbeddingConcurrency { get; init; } = 4;
}