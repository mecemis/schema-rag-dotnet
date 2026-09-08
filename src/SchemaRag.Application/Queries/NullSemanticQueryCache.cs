using SchemaRag.Application.Abstractions;

namespace SchemaRag.Application.Queries;

/// <summary>
/// Used when the semantic cache is switched off, so the query pipeline keeps a
/// single code path instead of branching on a nullable dependency.
/// </summary>
public sealed class NullSemanticQueryCache : ISemanticQueryCache
{
    public Task<SemanticCacheHit?> FindAsync(
        SemanticCacheLookup lookup,
        CancellationToken cancellationToken) =>
        Task.FromResult<SemanticCacheHit?>(null);

    public Task StoreAsync(
        SemanticCacheEntry entry,
        CancellationToken cancellationToken) => Task.CompletedTask;
}