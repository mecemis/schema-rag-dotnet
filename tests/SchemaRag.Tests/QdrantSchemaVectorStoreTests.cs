using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;
using SchemaRag.Domain.Schema;
using SchemaRag.Infrastructure.Configuration;
using SchemaRag.Infrastructure.Qdrant;

namespace SchemaRag.Tests;

public sealed class QdrantSchemaVectorStoreTests
{
    [Fact]
    public async Task SearchAsync_FiltersBySourceAndAllowedTablesAndRehydratesTrustedCatalog()
    {
        var customer = CreateSchema("sales", "customers");
        var handler = new RecordingHandler
        {
            QueryResponse = $$$"""
                {
                  "result": {
                    "points": [
                      { "payload": {
                        "dataSourceId": "sales",
                        "tableId": "{{{customer.TableId}}}",
                        "schemaName": "public",
                        "tableName": "customers",
                        "schemaVersion": "{{{SchemaDocumentVersion.Create(customer)}}}"
                      }},
                      { "payload": {
                        "dataSourceId": "warehouse",
                        "tableId": "{{{customer.TableId}}}",
                        "schemaName": "public",
                        "tableName": "customers",
                        "schemaVersion": "{{{SchemaDocumentVersion.Create(customer)}}}"
                      }}
                    ]
                  }
                }
                """
        };
        using var client = CreateClient(handler);
        var store = new QdrantSchemaVectorStore(
            client,
            Options.Create(new QdrantOptions()),
            new StubSchemaCatalog([customer]));

        var result = await store.SearchAsync(
            "sales",
            [customer.TableId],
            [0.1f, 0.2f],
            3,
            CancellationToken.None);

        Assert.Equal(customer, Assert.Single(result));
        using var request = JsonDocument.Parse(handler.QueryRequestBody);
        Assert.True(request.RootElement.GetProperty("with_payload").GetBoolean());
        Assert.False(request.RootElement.TryGetProperty("withPayload", out _));
        var conditions = request.RootElement.GetProperty("filter").GetProperty("must");
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "dataSourceId" &&
            condition.GetProperty("match").GetProperty("value").GetString() == "sales");
        Assert.Contains(conditions.EnumerateArray(), condition =>
            condition.GetProperty("key").GetString() == "tableId" &&
            condition.GetProperty("match").GetProperty("any")[0].GetString() == customer.TableId);
    }

    [Fact]
    public async Task ReplaceAsync_WritesNewPointsBeforeDeletingOnlyStaleSourcePoints()
    {
        var customer = CreateSchema("sales", "customers");
        var handler = new RecordingHandler();
        using var client = CreateClient(handler);
        var store = new QdrantSchemaVectorStore(
            client,
            Options.Create(new QdrantOptions()),
            new StubSchemaCatalog([customer]));
        var vector = new SchemaVector(
            customer,
            [0.1f, 0.2f],
            SchemaDocumentVersion.Create(customer));

        await store.ReplaceAsync("sales", [vector], CancellationToken.None);

        Assert.Equal(["upsert", "delete"], handler.Operations);

        using var delete = JsonDocument.Parse(handler.DeleteRequestBody);
        var deleteCondition = delete.RootElement.GetProperty("filter").GetProperty("must")[0];
        Assert.Equal("dataSourceId", deleteCondition.GetProperty("key").GetString());
        Assert.Equal("sales", deleteCondition.GetProperty("match").GetProperty("value").GetString());
        var retainedCondition = delete.RootElement.GetProperty("filter").GetProperty("must_not")[0];
        Assert.Equal("tableId", retainedCondition.GetProperty("key").GetString());
        Assert.Equal(
            customer.TableId,
            retainedCondition.GetProperty("match").GetProperty("any")[0].GetString());

        using var upsert = JsonDocument.Parse(handler.UpsertRequestBodies.Single());
        var point = upsert.RootElement.GetProperty("points")[0];
        Assert.Equal("sales", point.GetProperty("payload").GetProperty("dataSourceId").GetString());
        Assert.Equal(customer.TableId, point.GetProperty("payload").GetProperty("tableId").GetString());
        Assert.Equal("public", point.GetProperty("payload").GetProperty("schemaName").GetString());
        Assert.Equal("customers", point.GetProperty("payload").GetProperty("tableName").GetString());
    }

    [Fact]
    public async Task ReplaceAsync_WhenUpsertFails_DoesNotDeleteExistingSourcePoints()
    {
        var customer = CreateSchema("sales", "customers");
        var handler = new RecordingHandler { FailUpsert = true };
        using var client = CreateClient(handler);
        var store = new QdrantSchemaVectorStore(
            client,
            Options.Create(new QdrantOptions()),
            new StubSchemaCatalog([customer]));
        var vector = new SchemaVector(
            customer,
            [0.1f, 0.2f],
            SchemaDocumentVersion.Create(customer));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.ReplaceAsync("sales", [vector], CancellationToken.None));

        Assert.Equal(["upsert"], handler.Operations);
        Assert.Empty(handler.DeleteRequestBody);
    }

    [Fact]
    public async Task ReplaceAsync_SameTableInDifferentSources_UsesDifferentPointIds()
    {
        var sales = CreateSchema("sales", "customers");
        var warehouse = CreateSchema("warehouse", "customers");
        var handler = new RecordingHandler();
        using var client = CreateClient(handler);
        var store = new QdrantSchemaVectorStore(
            client,
            Options.Create(new QdrantOptions()),
            new StubSchemaCatalog([sales, warehouse]));

        await store.ReplaceAsync(
            "sales",
            [new SchemaVector(sales, [0.1f, 0.2f], SchemaDocumentVersion.Create(sales))],
            CancellationToken.None);
        await store.ReplaceAsync(
            "warehouse",
            [new SchemaVector(warehouse, [0.1f, 0.2f], SchemaDocumentVersion.Create(warehouse))],
            CancellationToken.None);

        var pointIds = handler.UpsertRequestBodies.Select(body =>
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("points")[0].GetProperty("id").GetString();
        }).ToArray();
        Assert.Equal(2, pointIds.Distinct(StringComparer.Ordinal).Count());
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://qdrant.test/") };

    private static SchemaDocument CreateSchema(string dataSourceId, string tableName) =>
        new(
            dataSourceId,
            "public",
            tableName,
            $"{tableName} business data.",
            [new SchemaColumn("id", "integer", "Primary key.", false, true)],
            []);

    private sealed class StubSchemaCatalog(IReadOnlyList<SchemaDocument> documents)
        : ISchemaCatalog
    {
        public Task<IReadOnlyList<SchemaDocument>> GetAllAsync(
            string dataSourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SchemaDocument>>(
                documents.Where(document => document.DataSourceId == dataSourceId).ToArray());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string QueryRequestBody { get; private set; } = string.Empty;
        public string DeleteRequestBody { get; private set; } = string.Empty;
        public List<string> UpsertRequestBodies { get; } = [];
        public List<string> Operations { get; } = [];
        public string QueryResponse { get; init; } = "{\"result\":{\"points\":[]}}";
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
                return JsonResponse(QueryResponse);
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
                if (FailUpsert)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return JsonResponse("{\"result\":{\"status\":\"completed\"}}");
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