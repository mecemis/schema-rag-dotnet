using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Infrastructure.Configuration;
using SchemaRag.Infrastructure.Qdrant;

namespace SchemaRag.Tests;

public sealed class QdrantSemanticQueryCacheTests
{
    private const string SchemaFingerprint = "2f1c";
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindAsync_ScopesTheLookupToTheSourceFingerprintAndSimilarityThreshold()
    {
        var handler = new RecordingHandler
        {
            QueryResponse = CreateQueryResponse(0.97)
        };
        var cache = CreateCache(handler, new SemanticCacheOptions
        {
            SimilarityThreshold = 0.93,
            TimeToLiveMinutes = 60
        });

        var hit = await cache.FindAsync(CreateLookup(), CancellationToken.None);

        Assert.NotNull(hit);
        Assert.Equal("SELECT name FROM public.customers LIMIT 5", hit.Sql);
        Assert.Equal("List customer names", hit.Question);
        Assert.Equal(["sales.public.customers"], hit.TableIds);
        Assert.Equal(0.97, hit.Similarity);

        using var request = JsonDocument.Parse(handler.QueryRequestBody);
        Assert.Equal(1, request.RootElement.GetProperty("limit").GetInt32());
        Assert.True(request.RootElement.GetProperty("with_payload").GetBoolean());
        Assert.Equal(0.93, request.RootElement.GetProperty("score_threshold").GetDouble());
        var conditions = request.RootElement.GetProperty("filter").GetProperty("must");
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "dataSourceId" &&
            condition.GetProperty("match").GetProperty("value").GetString() == "sales");
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "schemaFingerprint" &&
            condition.GetProperty("match").GetProperty("value").GetString() == SchemaFingerprint);
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "createdAtUnixSeconds" &&
            condition.GetProperty("range").GetProperty("gte").GetInt64() ==
                Now.AddMinutes(-60).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task FindAsync_WhenTimeToLiveIsDisabled_DoesNotFilterOnAge()
    {
        var handler = new RecordingHandler { QueryResponse = CreateQueryResponse(0.99) };
        var cache = CreateCache(handler, new SemanticCacheOptions { TimeToLiveMinutes = 0 });

        await cache.FindAsync(CreateLookup(), CancellationToken.None);

        using var request = JsonDocument.Parse(handler.QueryRequestBody);
        var conditions = request.RootElement.GetProperty("filter").GetProperty("must");
        Assert.DoesNotContain(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "createdAtUnixSeconds");
    }

    [Fact]
    public async Task FindAsync_WhenThePayloadBelongsToAnotherScope_ReportsAMiss()
    {
        var handler = new RecordingHandler
        {
            QueryResponse = CreateQueryResponse(0.99, schemaFingerprint: "another-fingerprint")
        };
        var cache = CreateCache(handler, new SemanticCacheOptions());

        Assert.Null(await cache.FindAsync(CreateLookup(), CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_WhenTheScoreIsBelowTheThreshold_ReportsAMiss()
    {
        var handler = new RecordingHandler { QueryResponse = CreateQueryResponse(0.80) };
        var cache = CreateCache(handler, new SemanticCacheOptions { SimilarityThreshold = 0.95 });

        Assert.Null(await cache.FindAsync(CreateLookup(), CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_WhenQdrantIsUnreachable_ReportsAMissInsteadOfFailingTheQuery()
    {
        var handler = new RecordingHandler { FailQuery = true };
        var cache = CreateCache(handler, new SemanticCacheOptions());

        Assert.Null(await cache.FindAsync(CreateLookup(), CancellationToken.None));
    }

    [Fact]
    public async Task StoreAsync_WritesOnePointAndPrunesEntriesPastTheTimeToLive()
    {
        var handler = new RecordingHandler();
        var cache = CreateCache(handler, new SemanticCacheOptions { TimeToLiveMinutes = 60 });

        await cache.StoreAsync(CreateEntry(), CancellationToken.None);

        Assert.Equal(["upsert", "delete"], handler.Operations);
        using var upsert = JsonDocument.Parse(handler.UpsertRequestBodies.Single());
        var payload = upsert.RootElement.GetProperty("points")[0].GetProperty("payload");
        Assert.Equal("sales", payload.GetProperty("dataSourceId").GetString());
        Assert.Equal(SchemaFingerprint, payload.GetProperty("schemaFingerprint").GetString());
        Assert.Equal("List customer names", payload.GetProperty("question").GetString());
        Assert.Equal(
            "SELECT name FROM public.customers LIMIT 5",
            payload.GetProperty("sql").GetString());
        Assert.Equal(
            Now.ToUnixTimeSeconds(),
            payload.GetProperty("createdAtUnixSeconds").GetInt64());

        using var delete = JsonDocument.Parse(handler.DeleteRequestBody);
        var conditions = delete.RootElement.GetProperty("filter").GetProperty("must");
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "createdAtUnixSeconds" &&
            condition.GetProperty("range").GetProperty("lt").GetInt64() ==
                Now.AddMinutes(-60).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task StoreAsync_WhenTimeToLiveIsDisabled_KeepsEveryEntry()
    {
        var handler = new RecordingHandler();
        var cache = CreateCache(handler, new SemanticCacheOptions { TimeToLiveMinutes = 0 });

        await cache.StoreAsync(CreateEntry(), CancellationToken.None);

        Assert.Equal(["upsert"], handler.Operations);
    }

    [Fact]
    public async Task StoreAsync_WhenTheSameQuestionIsAskedAgain_OverwritesTheExistingPoint()
    {
        var handler = new RecordingHandler();
        var cache = CreateCache(handler, new SemanticCacheOptions { TimeToLiveMinutes = 0 });

        await cache.StoreAsync(CreateEntry(question: "List customer names"), CancellationToken.None);
        await cache.StoreAsync(CreateEntry(question: "  List   Customer   Names  "), CancellationToken.None);
        await cache.StoreAsync(CreateEntry(question: "List order totals"), CancellationToken.None);

        var pointIds = handler.UpsertRequestBodies.Select(body =>
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("points")[0].GetProperty("id").GetString();
        }).ToArray();
        Assert.Equal(pointIds[0], pointIds[1]);
        Assert.NotEqual(pointIds[0], pointIds[2]);
    }

    [Fact]
    public async Task StoreAsync_WhenQdrantRejectsTheWrite_LeavesTheAnsweredQueryAlone()
    {
        var handler = new RecordingHandler { FailUpsert = true };
        var cache = CreateCache(handler, new SemanticCacheOptions());

        await cache.StoreAsync(CreateEntry(), CancellationToken.None);

        Assert.Equal(["upsert"], handler.Operations);
        Assert.Empty(handler.DeleteRequestBody);
    }

    private static QdrantSemanticQueryCache CreateCache(
        HttpMessageHandler handler,
        SemanticCacheOptions options) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://qdrant.test/") },
            Options.Create(options),
            new StubTimeProvider(Now),
            NullLogger<QdrantSemanticQueryCache>.Instance);

    private static SemanticCacheLookup CreateLookup() =>
        new("sales", SchemaFingerprint, [0.1f, 0.2f]);

    private static SemanticCacheEntry CreateEntry(string question = "List customer names") =>
        new(
            "sales",
            SchemaFingerprint,
            question,
            [0.1f, 0.2f],
            "SELECT name FROM public.customers LIMIT 5",
            ["sales.public.customers"]);

    private static string CreateQueryResponse(
        double score,
        string schemaFingerprint = SchemaFingerprint)
    {
        var invariantScore = score.ToString(CultureInfo.InvariantCulture);
        return $$"""
        {
          "result": {
            "points": [
              {
                "id": "b8c0f6f0-0000-4000-8000-000000000000",
                "score": {{invariantScore}},
                "payload": {
                  "dataSourceId": "sales",
                  "schemaFingerprint": "{{schemaFingerprint}}",
                  "question": "List customer names",
                  "sql": "SELECT name FROM public.customers LIMIT 5",
                  "tableIds": ["sales.public.customers"],
                  "createdAtUnixSeconds": 1
                }
              }
            ]
          }
        }
        """;
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string QueryRequestBody { get; private set; } = string.Empty;
        public string DeleteRequestBody { get; private set; } = string.Empty;
        public List<string> UpsertRequestBodies { get; } = [];
        public List<string> Operations { get; } = [];
        public string QueryResponse { get; init; } = "{\"result\":{\"points\":[]}}";
        public bool FailQuery { get; init; }
        public bool FailUpsert { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""
                    {
                      "result": {
                        "config": {
                          "params": {
                            "vectors": { "size": 2, "distance": "Cosine" }
                          }
                        }
                      }
                    }
                    """);
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.Contains("/points/query", StringComparison.Ordinal))
            {
                QueryRequestBody = body;
                return FailQuery
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : JsonResponse(QueryResponse);
            }

            if (path.Contains("/points/delete", StringComparison.Ordinal))
            {
                Operations.Add("delete");
                DeleteRequestBody = body;
                return JsonResponse("{\"result\":{\"status\":\"completed\"}}");
            }

            if (request.Method == HttpMethod.Put && path.Contains("/points", StringComparison.Ordinal))
            {
                Operations.Add("upsert");
                UpsertRequestBodies.Add(body);
                return FailUpsert
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : JsonResponse("{\"result\":{\"status\":\"completed\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}