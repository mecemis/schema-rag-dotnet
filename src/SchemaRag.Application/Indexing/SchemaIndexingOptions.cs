namespace SchemaRag.Application.Indexing;

public sealed record SchemaIndexingOptions(
    int MaxTablesPerIndex,
    int EmbeddingConcurrency);