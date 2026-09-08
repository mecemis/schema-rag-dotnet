using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SchemaRag.Infrastructure.Qdrant;

/// <summary>
/// REST contracts and collection bootstrap shared by the schema vector store and
/// the semantic query cache. Both keep one collection of cosine-distance points
/// sized to the embedding model in use.
/// </summary>
internal static class QdrantCollection
{
    public static async Task EnsureAsync(
        HttpClient httpClient,
        string collectionName,
        int vectorSize,
        CancellationToken cancellationToken)
    {
        using var getResponse = await httpClient.GetAsync(
            $"collections/{collectionName}",
            cancellationToken);

        if (getResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var createResponse = await httpClient.PutAsJsonAsync(
                $"collections/{collectionName}",
                new CreateCollectionRequest(new VectorConfiguration(vectorSize, "Cosine")),
                cancellationToken);
            createResponse.EnsureSuccessStatusCode();
            return;
        }

        getResponse.EnsureSuccessStatusCode();
        var collection = await getResponse.Content.ReadFromJsonAsync<CollectionResponse>(
            cancellationToken);
        var existingSize = collection?.Result?.Config?.Parameters?.Vectors?.Size;
        if (existingSize != vectorSize)
        {
            throw new InvalidOperationException(
                $"Qdrant collection '{collectionName}' expects vectors of size {existingSize}; " +
                $"received {vectorSize}. Recreate the collection after changing embedding models.");
        }
    }

    internal sealed record UpsertRequest<TPoint>(IReadOnlyList<TPoint> Points);

    internal sealed record DeleteRequest(QdrantFilter Filter);

    internal sealed record QdrantFilter(
        IReadOnlyList<QdrantCondition> Must,
        [property: JsonPropertyName("must_not"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<QdrantCondition>? MustNot = null);

    internal sealed record QdrantCondition(
        string Key,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        MatchCondition? Match = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        RangeCondition? Range = null);

    internal sealed record MatchCondition(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Any = null);

    internal sealed record RangeCondition(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Gte = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Lt = null);

    private sealed record CreateCollectionRequest(VectorConfiguration Vectors);
    private sealed record VectorConfiguration(int Size, string Distance);
    private sealed record CollectionResponse(CollectionResult Result);
    private sealed record CollectionResult(CollectionConfig Config);

    private sealed record CollectionConfig(CollectionParameters Params)
    {
        public CollectionParameters Parameters => Params;
    }

    private sealed record CollectionParameters(VectorConfiguration Vectors);
}