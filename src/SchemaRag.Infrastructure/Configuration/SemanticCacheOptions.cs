namespace SchemaRag.Infrastructure.Configuration;

public sealed class SemanticCacheOptions
{
    public const string SectionName = "SemanticCache";

    public bool Enabled { get; init; } = true;
    public string CollectionName { get; init; } = "semantic_query_cache";

    /// <summary>
    /// Minimum cosine similarity between the incoming question and a stored one
    /// before its SQL is reused. Lower values reuse more aggressively and answer
    /// more questions with a statement that was written for a different one.
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.95;

    /// <summary>
    /// How long an entry stays eligible for reuse. Zero keeps entries until the
    /// schema they were written against changes.
    /// </summary>
    public int TimeToLiveMinutes { get; init; } = 1440;
}