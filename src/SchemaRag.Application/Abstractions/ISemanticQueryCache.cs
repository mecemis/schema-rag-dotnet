namespace SchemaRag.Application.Abstractions;

/// <summary>
/// Remembers the SQL that was generated for a question so that a later question
/// with the same meaning can skip the language model. Only the statement is
/// stored; rows are always read from the database again.
/// </summary>
public interface ISemanticQueryCache
{
    Task<SemanticCacheHit?> FindAsync(
        SemanticCacheLookup lookup,
        CancellationToken cancellationToken);

    Task StoreAsync(
        SemanticCacheEntry entry,
        CancellationToken cancellationToken);
}

public sealed record SemanticCacheLookup(
    string DataSourceId,
    string SchemaFingerprint,
    float[] QuestionEmbedding);

public sealed record SemanticCacheEntry(
    string DataSourceId,
    string SchemaFingerprint,
    string Question,
    float[] QuestionEmbedding,
    string Sql,
    IReadOnlyList<string> TableIds);

public sealed record SemanticCacheHit(
    string Question,
    string Sql,
    IReadOnlyList<string> TableIds,
    double Similarity);