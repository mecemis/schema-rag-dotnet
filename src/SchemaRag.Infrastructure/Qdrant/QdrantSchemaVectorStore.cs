using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;
using SchemaRag.Domain.Schema;
using SchemaRag.Infrastructure.Configuration;

namespace SchemaRag.Infrastructure.Qdrant;

public sealed class QdrantSchemaVectorStore(
    HttpClient httpClient,
    IOptions<QdrantOptions> options,
    ISchemaCatalog schemaCatalog) : ISchemaVectorStore
{
    public async Task<IReadOnlyList<SchemaDocument>> SearchAsync(
        string dataSourceId,
        IReadOnlyCollection<string> allowedTableIds,
        float[] embedding,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(allowedTableIds);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (allowedTableIds.Count == 0)
        {
            return [];
        }

        await QdrantCollection.EnsureAsync(
            httpClient,
            options.Value.CollectionName,
            embedding.Length,
            cancellationToken);
        using var response = await httpClient.PostAsJsonAsync(
            $"collections/{options.Value.CollectionName}/points/query",
            new QueryRequest(
                embedding,
                limit,
                true,
                CreateSearchFilter(dataSourceId, allowedTableIds)),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(
            cancellationToken);
        var catalog = await schemaCatalog.GetAllAsync(dataSourceId, cancellationToken);
        var allowedIds = allowedTableIds.ToHashSet(StringComparer.Ordinal);
        var documentsById = catalog
            .Where(document => allowedIds.Contains(document.TableId))
            .ToDictionary(document => document.TableId, StringComparer.Ordinal);

        return result?.Result?.Points
            .Select(point => point.Payload)
            .Where(payload =>
                payload is not null &&
                string.Equals(payload.DataSourceId, dataSourceId, StringComparison.Ordinal) &&
                allowedIds.Contains(payload.TableId) &&
                documentsById.TryGetValue(payload.TableId, out var document) &&
                string.Equals(
                    payload.SchemaVersion,
                    SchemaDocumentVersion.Create(document),
                    StringComparison.Ordinal))
            .Select(payload => documentsById[payload!.TableId])
            .DistinctBy(document => document.TableId)
            .ToArray() ?? [];
    }

    public async Task ReplaceAsync(
        string dataSourceId,
        IReadOnlyList<SchemaVector> schemaVectors,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(schemaVectors);
        if (schemaVectors.Count == 0)
        {
            throw new ArgumentException("At least one schema vector is required.", nameof(schemaVectors));
        }

        if (schemaVectors.Any(vector =>
                !string.Equals(vector.Document.DataSourceId, dataSourceId, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Every schema vector must belong to the requested data source.",
                nameof(schemaVectors));
        }

        var vectorSize = schemaVectors[0].Embedding.Length;
        if (vectorSize == 0 || schemaVectors.Any(vector => vector.Embedding.Length != vectorSize))
        {
            throw new InvalidOperationException("All schema embeddings must have the same non-zero size.");
        }

        await QdrantCollection.EnsureAsync(
            httpClient,
            options.Value.CollectionName,
            vectorSize,
            cancellationToken);
        var points = schemaVectors.Select(vector =>
            new QdrantPoint(
                CreatePointId(dataSourceId, vector.Document.TableId),
                vector.Embedding,
                new QdrantPayload(
                    dataSourceId,
                    vector.Document.TableId,
                    vector.Document.SchemaName,
                    vector.Document.TableName,
                    vector.SchemaVersion))).ToArray();
        using var response = await httpClient.PutAsJsonAsync(
            $"collections/{options.Value.CollectionName}/points?wait=true",
            new QdrantCollection.UpsertRequest<QdrantPoint>(points),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await DeleteStaleSourcePointsAsync(
            dataSourceId,
            schemaVectors.Select(vector => vector.Document.TableId).ToArray(),
            cancellationToken);
    }

    private async Task DeleteStaleSourcePointsAsync(
        string dataSourceId,
        IReadOnlyCollection<string> retainedTableIds,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"collections/{options.Value.CollectionName}/points/delete?wait=true",
            new QdrantCollection.DeleteRequest(
                CreateStaleSourceFilter(dataSourceId, retainedTableIds)),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static QdrantCollection.QdrantFilter CreateSearchFilter(
        string dataSourceId,
        IReadOnlyCollection<string> allowedTableIds) =>
        new([
            new QdrantCollection.QdrantCondition(
                "dataSourceId",
                new QdrantCollection.MatchCondition(Value: dataSourceId)),
            new QdrantCollection.QdrantCondition(
                "tableId",
                new QdrantCollection.MatchCondition(Any: allowedTableIds.ToArray()))
        ]);

    private static QdrantCollection.QdrantFilter CreateStaleSourceFilter(
        string dataSourceId,
        IReadOnlyCollection<string> retainedTableIds) =>
        new(
            [
                new QdrantCollection.QdrantCondition(
                    "dataSourceId",
                    new QdrantCollection.MatchCondition(Value: dataSourceId))
            ],
            [
                new QdrantCollection.QdrantCondition(
                    "tableId",
                    new QdrantCollection.MatchCondition(Any: retainedTableIds.ToArray()))
            ]);

    private static string CreatePointId(string dataSourceId, string tableId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{dataSourceId}\u001f{tableId}"));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

    private sealed record QueryRequest(
        float[] Query,
        int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        QdrantCollection.QdrantFilter Filter);
    private sealed record SearchResponse(QueryResult? Result);
    private sealed record QueryResult(IReadOnlyList<SearchPoint> Points);
    private sealed record SearchPoint(QdrantPayload? Payload);
    private sealed record QdrantPoint(string Id, float[] Vector, QdrantPayload Payload);
    private sealed record QdrantPayload(
        string DataSourceId,
        string TableId,
        string SchemaName,
        string TableName,
        string SchemaVersion);
}