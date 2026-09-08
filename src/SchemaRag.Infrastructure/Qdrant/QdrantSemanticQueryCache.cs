using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Queries;
using SchemaRag.Infrastructure.Configuration;

namespace SchemaRag.Infrastructure.Qdrant;

/// <summary>
/// Keeps generated statements in their own Qdrant collection, keyed by the
/// question embedding and scoped to a data source and schema fingerprint.
/// The cache is an optimisation, never a dependency: when Qdrant is unreachable
/// the lookup reports a miss and the write is dropped, so a query still answers
/// through the language model.
/// </summary>
public sealed class QdrantSemanticQueryCache(
    HttpClient httpClient,
    IOptions<SemanticCacheOptions> options,
    TimeProvider timeProvider,
    ILogger<QdrantSemanticQueryCache> logger) : ISemanticQueryCache
{
    public async Task<SemanticCacheHit?> FindAsync(
        SemanticCacheLookup lookup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentException.ThrowIfNullOrWhiteSpace(lookup.DataSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lookup.SchemaFingerprint);
        if (lookup.QuestionEmbedding.Length == 0)
        {
            throw new ArgumentException("A question embedding is required.", nameof(lookup));
        }

        try
        {
            await QdrantCollection.EnsureAsync(
                httpClient,
                options.Value.CollectionName,
                lookup.QuestionEmbedding.Length,
                cancellationToken);
            using var response = await httpClient.PostAsJsonAsync(
                $"collections/{options.Value.CollectionName}/points/query",
                new QueryRequest(
                    lookup.QuestionEmbedding,
                    1,
                    true,
                    options.Value.SimilarityThreshold,
                    CreateLookupFilter(lookup.DataSourceId, lookup.SchemaFingerprint)),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<QueryResponse>(cancellationToken);
            var point = result?.Result?.Points.FirstOrDefault();

            return IsUsable(point, lookup)
                ? new SemanticCacheHit(
                    point!.Payload!.Question,
                    point.Payload.Sql,
                    point.Payload.TableIds,
                    point.Score)
                : null;
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            logger.LogWarning(
                exception,
                "Semantic cache lookup failed for {DataSourceId}; answering without the cache.",
                lookup.DataSourceId);
            return null;
        }
    }

    public async Task StoreAsync(
        SemanticCacheEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.DataSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.SchemaFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Sql);
        if (entry.QuestionEmbedding.Length == 0)
        {
            throw new ArgumentException("A question embedding is required.", nameof(entry));
        }

        try
        {
            await QdrantCollection.EnsureAsync(
                httpClient,
                options.Value.CollectionName,
                entry.QuestionEmbedding.Length,
                cancellationToken);
            var point = new CachePoint(
                CreatePointId(entry),
                entry.QuestionEmbedding,
                new CachePayload(
                    entry.DataSourceId,
                    entry.SchemaFingerprint,
                    entry.Question,
                    entry.Sql,
                    entry.TableIds,
                    timeProvider.GetUtcNow().ToUnixTimeSeconds()));
            using var response = await httpClient.PutAsJsonAsync(
                $"collections/{options.Value.CollectionName}/points?wait=true",
                new QdrantCollection.UpsertRequest<CachePoint>([point]),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await DeleteExpiredPointsAsync(entry.DataSourceId, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            logger.LogWarning(
                exception,
                "Semantic cache write failed for {DataSourceId}; the answer was still returned.",
                entry.DataSourceId);
        }
    }

    /// <summary>
    /// Expired points are already filtered out of every lookup; removing them on
    /// the next write keeps the collection from growing without bound.
    /// </summary>
    private async Task DeleteExpiredPointsAsync(
        string dataSourceId,
        CancellationToken cancellationToken)
    {
        var cutoff = ResolveExpiryCutoff();
        if (cutoff is null)
        {
            return;
        }

        using var response = await httpClient.PostAsJsonAsync(
            $"collections/{options.Value.CollectionName}/points/delete?wait=true",
            new QdrantCollection.DeleteRequest(new QdrantCollection.QdrantFilter([
                new QdrantCollection.QdrantCondition(
                    "dataSourceId",
                    new QdrantCollection.MatchCondition(Value: dataSourceId)),
                new QdrantCollection.QdrantCondition(
                    "createdAtUnixSeconds",
                    Range: new QdrantCollection.RangeCondition(Lt: cutoff))
            ])),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private QdrantCollection.QdrantFilter CreateLookupFilter(
        string dataSourceId,
        string schemaFingerprint)
    {
        var conditions = new List<QdrantCollection.QdrantCondition>
        {
            new("dataSourceId", new QdrantCollection.MatchCondition(Value: dataSourceId)),
            new("schemaFingerprint", new QdrantCollection.MatchCondition(Value: schemaFingerprint))
        };

        if (ResolveExpiryCutoff() is { } cutoff)
        {
            conditions.Add(new QdrantCollection.QdrantCondition(
                "createdAtUnixSeconds",
                Range: new QdrantCollection.RangeCondition(Gte: cutoff)));
        }

        return new QdrantCollection.QdrantFilter(conditions);
    }

    private double? ResolveExpiryCutoff() =>
        options.Value.TimeToLiveMinutes <= 0
            ? null
            : timeProvider.GetUtcNow()
                .AddMinutes(-options.Value.TimeToLiveMinutes)
                .ToUnixTimeSeconds();

    /// <summary>
    /// Qdrant is shared storage, so a returned payload is treated as untrusted
    /// input and re-checked against the scope that was asked for.
    /// </summary>
    private bool IsUsable(CachePoint? point, SemanticCacheLookup lookup) =>
        point?.Payload is not null &&
        point.Score >= options.Value.SimilarityThreshold &&
        string.Equals(point.Payload.DataSourceId, lookup.DataSourceId, StringComparison.Ordinal) &&
        string.Equals(point.Payload.SchemaFingerprint, lookup.SchemaFingerprint, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(point.Payload.Sql) &&
        point.Payload.TableIds.Count > 0;

    private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException or JsonException or InvalidOperationException => true,
            _ => false
        };

    private static string CreatePointId(SemanticCacheEntry entry)
    {
        var key = string.Join(
            '\u001f',
            entry.DataSourceId,
            entry.SchemaFingerprint,
            SemanticCacheKey.NormalizeQuestion(entry.Question));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

    private sealed record QueryRequest(
        float[] Query,
        int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        [property: JsonPropertyName("score_threshold")] double ScoreThreshold,
        QdrantCollection.QdrantFilter Filter);

    private sealed record QueryResponse(QueryResult? Result);
    private sealed record QueryResult(IReadOnlyList<CachePoint> Points);
    private sealed record CachePoint(string Id, float[] Vector, CachePayload? Payload, double Score = 0);

    private sealed record CachePayload(
        string DataSourceId,
        string SchemaFingerprint,
        string Question,
        string Sql,
        IReadOnlyList<string> TableIds,
        long CreatedAtUnixSeconds);
}