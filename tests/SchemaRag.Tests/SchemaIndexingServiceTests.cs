using SchemaRag.Application.Abstractions;
using SchemaRag.Application.DataSources;
using SchemaRag.Application.Indexing;
using SchemaRag.Domain.DataSources;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class SchemaIndexingServiceTests
{
    private const string SalesDataSourceId = "sales";

    [Fact]
    public async Task IndexAsync_EmbedsAndReplacesOnlySelectedTablesForTheRequestedSource()
    {
        var customer = CreateSchema("customers");
        var order = CreateSchema("orders");
        var product = CreateSchema("products");
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([customer, order, product]);
        var embedding = new RecordingEmbeddingService();
        var store = new RecordingSchemaVectorStore();
        var service = CreateService(registry, catalog, embedding, store);

        var result = await service.IndexAsync(
            SalesDataSourceId,
            [order.TableId, customer.TableId],
            CancellationToken.None);

        Assert.Equal(SalesDataSourceId, registry.LastRequestedId);
        Assert.Equal(SalesDataSourceId, catalog.LastDataSourceId);
        Assert.Equal(2, embedding.CallCount);
        Assert.Contains(
            embedding.Texts,
            text => text.Contains(customer.QualifiedTableName, StringComparison.Ordinal));
        Assert.Contains(
            embedding.Texts,
            text => text.Contains(order.QualifiedTableName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            embedding.Texts,
            text => text.Contains(product.QualifiedTableName, StringComparison.Ordinal));
        Assert.Equal(1, store.ReplaceCallCount);
        Assert.Equal(SalesDataSourceId, store.LastReplaceDataSourceId);
        Assert.Equal(
            [order.TableId, customer.TableId],
            store.LastVectors!.Select(vector => vector.Document.TableId));
        Assert.All(store.LastVectors!, vector => Assert.NotEmpty(vector.Embedding));
        Assert.All(store.LastVectors!, vector => Assert.NotEmpty(vector.SchemaVersion));
        Assert.Equal(SalesDataSourceId, result.DataSourceId);
        Assert.Equal(2, result.IndexedCount);
        Assert.Equal(
            [order.QualifiedTableName, customer.QualifiedTableName],
            result.Tables);
    }

    [Fact]
    public async Task IndexAsync_WhenDataSourceIsUnknown_DoesNotCallAnyDownstreamDependency()
    {
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([]);
        var embedding = new RecordingEmbeddingService();
        var store = new RecordingSchemaVectorStore();
        var service = CreateService(registry, catalog, embedding, store);

        await Assert.ThrowsAsync<DataSourceNotFoundException>(() => service.IndexAsync(
            "unknown",
            ["tbl_unknown"],
            CancellationToken.None));

        Assert.Equal(1, registry.CallCount);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, embedding.CallCount);
        Assert.Equal(0, store.ReplaceCallCount);
    }

    [Fact]
    public async Task IndexAsync_WhenSelectedTableIsUnknown_DoesNotEmbedOrReplace()
    {
        var customer = CreateSchema("customers");
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([customer]);
        var embedding = new RecordingEmbeddingService();
        var store = new RecordingSchemaVectorStore();
        var service = CreateService(registry, catalog, embedding, store);
        var unknownTableId = SchemaTableIdentity.CreateId(
            SalesDataSourceId,
            "public",
            "missing_table");

        await Assert.ThrowsAsync<SchemaSelectionException>(() => service.IndexAsync(
            SalesDataSourceId,
            [customer.TableId, unknownTableId],
            CancellationToken.None));

        Assert.Equal(1, catalog.CallCount);
        Assert.Equal(0, embedding.CallCount);
        Assert.Equal(0, store.ReplaceCallCount);
    }

    [Fact]
    public async Task IndexAsync_WhenAnyEmbeddingFails_DoesNotReplaceTheExistingSourceIndex()
    {
        var customer = CreateSchema("customers");
        var order = CreateSchema("orders");
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([customer, order]);
        var embedding = new RecordingEmbeddingService(text =>
            text.Contains(order.QualifiedTableName, StringComparison.Ordinal)
                ? throw new HttpRequestException("Embedding dependency failed.")
                : [0.1f, 0.2f]);
        var store = new RecordingSchemaVectorStore();
        var service = CreateService(registry, catalog, embedding, store);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.IndexAsync(
            SalesDataSourceId,
            [customer.TableId, order.TableId],
            CancellationToken.None));

        Assert.Equal(2, embedding.CallCount);
        Assert.Equal(0, store.ReplaceCallCount);
        Assert.Null(store.LastReplaceDataSourceId);
        Assert.Null(store.LastVectors);
    }

    private static SchemaIndexingService CreateService(
        IDataSourceRegistry registry,
        ISchemaCatalog catalog,
        IEmbeddingService embedding,
        ISchemaVectorStore store) =>
        new(
            registry,
            catalog,
            embedding,
            store,
            new SchemaIndexingOptions(
                MaxTablesPerIndex: 10,
                EmbeddingConcurrency: 2));

    private static DataSourceDescriptor CreateDataSource(string id) =>
        new(id, $"{id} database", IsDefault: id == SalesDataSourceId);

    private static SchemaDocument CreateSchema(string tableName) =>
        new(
            SalesDataSourceId,
            "public",
            tableName,
            $"Business data stored in public.{tableName}.",
            [new SchemaColumn("name", "text", "Display name.", false)],
            []);

    private sealed class RecordingDataSourceRegistry(params DataSourceDescriptor[] dataSources)
        : IDataSourceRegistry
    {
        private readonly IReadOnlyDictionary<string, DataSourceDescriptor> _dataSources =
            dataSources.ToDictionary(source => source.Id, StringComparer.Ordinal);

        public string DefaultDataSourceId => dataSources.Single(source => source.IsDefault).Id;
        public int CallCount { get; private set; }
        public string? LastRequestedId { get; private set; }

        public IReadOnlyList<DataSourceDescriptor> GetAll() => dataSources;

        public DataSourceDescriptor GetRequired(string dataSourceId)
        {
            CallCount++;
            LastRequestedId = dataSourceId;
            return _dataSources.TryGetValue(dataSourceId, out var source)
                ? source
                : throw new DataSourceNotFoundException(dataSourceId);
        }
    }

    private sealed class RecordingSchemaCatalog(IReadOnlyList<SchemaDocument> documents)
        : ISchemaCatalog
    {
        public int CallCount { get; private set; }
        public string? LastDataSourceId { get; private set; }

        public Task<IReadOnlyList<SchemaDocument>> GetAllAsync(
            string dataSourceId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDataSourceId = dataSourceId;
            return Task.FromResult(documents);
        }
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        private readonly Func<string, float[]> _generate;

        public RecordingEmbeddingService(Func<string, float[]>? generate = null)
        {
            _generate = generate ?? (_ => [0.1f, 0.2f]);
        }

        public int CallCount { get; private set; }
        public List<string> Texts { get; } = [];

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken)
        {
            CallCount++;
            Texts.Add(text);
            return Task.FromResult(_generate(text));
        }
    }

    private sealed class RecordingSchemaVectorStore : ISchemaVectorStore
    {
        public int ReplaceCallCount { get; private set; }
        public string? LastReplaceDataSourceId { get; private set; }
        public IReadOnlyList<SchemaVector>? LastVectors { get; private set; }

        public Task<IReadOnlyList<SchemaDocument>> SearchAsync(
            string dataSourceId,
            IReadOnlyCollection<string> allowedTableIds,
            float[] embedding,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SchemaDocument>>([]);

        public Task ReplaceAsync(
            string dataSourceId,
            IReadOnlyList<SchemaVector> schemaVectors,
            CancellationToken cancellationToken)
        {
            ReplaceCallCount++;
            LastReplaceDataSourceId = dataSourceId;
            LastVectors = schemaVectors;
            return Task.CompletedTask;
        }
    }
}