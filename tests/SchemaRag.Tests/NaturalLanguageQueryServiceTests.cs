using SchemaRag.Application.Abstractions;
using SchemaRag.Application.DataSources;
using SchemaRag.Application.Indexing;
using SchemaRag.Application.Prompts;
using SchemaRag.Application.Queries;
using SchemaRag.Application.Sql;
using SchemaRag.Domain.DataSources;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class NaturalLanguageQueryServiceTests
{
    private const string SalesDataSourceId = "sales";

    [Fact]
    public async Task AskAsync_PropagatesDataSourceAndAllowedTablesThroughThePipeline()
    {
        using var cancellation = new CancellationTokenSource();
        var customer = CreateSchema(SalesDataSourceId, "public", "customers");
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([customer]);
        var embedding = new RecordingEmbeddingService([0.1f, 0.2f]);
        var store = new RecordingSchemaVectorStore([customer]);
        var chat = new RecordingChatCompletionService(
            $"SELECT name FROM {customer.QualifiedTableName} LIMIT 5");
        var executor = new RecordingQueryExecutor(
            new QueryExecutionResult(
                ["name"],
                [new Dictionary<string, object?> { ["name"] = "Ada" }],
                12));
        var service = CreateService(
            registry,
            catalog,
            embedding,
            store,
            chat,
            executor,
            retrievalCount: 3);

        var result = await service.AskAsync(
            SalesDataSourceId,
            "  Show customer names  ",
            [customer.TableId],
            cancellation.Token);

        Assert.Equal(SalesDataSourceId, registry.LastRequestedId);
        Assert.Equal(SalesDataSourceId, catalog.LastDataSourceId);
        Assert.Equal("Show customer names", embedding.LastText);
        Assert.Equal(SalesDataSourceId, store.LastSearchDataSourceId);
        Assert.Equal(customer.TableId, Assert.Single(store.LastAllowedTableIds!));
        Assert.Equal([0.1f, 0.2f], Assert.IsType<float[]>(store.LastVector));
        Assert.Equal(3, store.LastLimit);
        Assert.Contains(customer.QualifiedTableName, chat.LastPrompt, StringComparison.Ordinal);
        Assert.Equal(SalesDataSourceId, executor.LastDataSourceId);
        Assert.Equal(
            $"SELECT name FROM {customer.QualifiedTableName} LIMIT 5",
            executor.LastSql?.Value);
        Assert.Equal(SalesDataSourceId, result.DataSourceId);
        Assert.Equal([customer.QualifiedTableName], result.RetrievedTables);
        Assert.Equal(["name"], result.Columns);
        Assert.Equal("Ada", result.Rows[0]["name"]);
        Assert.Equal(12, result.ExecutionTimeMs);
        Assert.Equal(cancellation.Token, executor.LastCancellationToken);
    }

    [Fact]
    public async Task AskAsync_WhenDataSourceIsUnknown_DoesNotCallAnyDownstreamDependency()
    {
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([]);
        var embedding = new RecordingEmbeddingService([0.1f]);
        var store = new RecordingSchemaVectorStore([]);
        var chat = new RecordingChatCompletionService("SELECT 1 LIMIT 1");
        var executor = new RecordingQueryExecutor(new QueryExecutionResult([], [], 0));
        var service = CreateService(registry, catalog, embedding, store, chat, executor);

        await Assert.ThrowsAsync<DataSourceNotFoundException>(() => service.AskAsync(
            "unknown",
            "Show customer names",
            null,
            CancellationToken.None));

        Assert.Equal(1, registry.CallCount);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, embedding.CallCount);
        Assert.Equal(0, store.SearchCallCount);
        Assert.Equal(0, chat.CallCount);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task AskAsync_WhenSelectedTableIsUnknown_DoesNotCallTheQueryPipeline()
    {
        var customer = CreateSchema(SalesDataSourceId, "public", "customers");
        var registry = new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId));
        var catalog = new RecordingSchemaCatalog([customer]);
        var embedding = new RecordingEmbeddingService([0.1f]);
        var store = new RecordingSchemaVectorStore([]);
        var chat = new RecordingChatCompletionService("SELECT 1 LIMIT 1");
        var executor = new RecordingQueryExecutor(new QueryExecutionResult([], [], 0));
        var service = CreateService(registry, catalog, embedding, store, chat, executor);
        var unknownTableId = SchemaTableIdentity.CreateId(
            SalesDataSourceId,
            "public",
            "missing_table");

        await Assert.ThrowsAsync<SchemaSelectionException>(() => service.AskAsync(
            SalesDataSourceId,
            "Show missing data",
            [unknownTableId],
            CancellationToken.None));

        Assert.Equal(1, catalog.CallCount);
        Assert.Equal(0, embedding.CallCount);
        Assert.Equal(0, store.SearchCallCount);
        Assert.Equal(0, chat.CallCount);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task AskAsync_WhenGeneratedSqlIsDangerous_DoesNotExecute()
    {
        var customer = CreateSchema(SalesDataSourceId, "public", "customers");
        var executor = new RecordingQueryExecutor(new QueryExecutionResult([], [], 0));
        var service = CreateService(
            new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId)),
            new RecordingSchemaCatalog([customer]),
            new RecordingEmbeddingService([0.1f]),
            new RecordingSchemaVectorStore([customer]),
            new RecordingChatCompletionService("DROP TABLE public.customers"),
            executor);

        await Assert.ThrowsAsync<UnsafeSqlException>(() => service.AskAsync(
            SalesDataSourceId,
            "Delete all customers",
            [customer.TableId],
            CancellationToken.None));

        Assert.Equal(0, executor.CallCount);
        Assert.Null(executor.LastDataSourceId);
    }

    [Fact]
    public async Task AskAsync_WhenGeneratedSqlReferencesTableOutsideAllowList_DoesNotExecute()
    {
        var customer = CreateSchema(SalesDataSourceId, "public", "customers");
        var order = CreateSchema(SalesDataSourceId, "public", "orders");
        var executor = new RecordingQueryExecutor(new QueryExecutionResult([], [], 0));
        var service = CreateService(
            new RecordingDataSourceRegistry(CreateDataSource(SalesDataSourceId)),
            new RecordingSchemaCatalog([customer, order]),
            new RecordingEmbeddingService([0.1f]),
            new RecordingSchemaVectorStore([customer]),
            new RecordingChatCompletionService(
                $"SELECT total_amount FROM {order.QualifiedTableName} LIMIT 5"),
            executor);

        await Assert.ThrowsAsync<UnsafeSqlException>(() => service.AskAsync(
            SalesDataSourceId,
            "Show customer names",
            [customer.TableId],
            CancellationToken.None));

        Assert.Equal(0, executor.CallCount);
        Assert.Null(executor.LastDataSourceId);
    }

    private static NaturalLanguageQueryService CreateService(
        IDataSourceRegistry registry,
        ISchemaCatalog catalog,
        IEmbeddingService embedding,
        ISchemaVectorStore store,
        IChatCompletionService chat,
        IGeneratedQueryExecutor executor,
        int retrievalCount = 4) =>
        new(
            registry,
            catalog,
            embedding,
            store,
            chat,
            new SqlPromptBuilder(100),
            new SqlQueryValidator(100),
            executor,
            new QueryPipelineOptions(retrievalCount));

    private static DataSourceDescriptor CreateDataSource(string id) =>
        new(id, $"{id} database", IsDefault: id == SalesDataSourceId);

    private static SchemaDocument CreateSchema(
        string dataSourceId,
        string schemaName,
        string tableName) =>
        new(
            dataSourceId,
            schemaName,
            tableName,
            $"Business data stored in {schemaName}.{tableName}.",
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

    private sealed class RecordingEmbeddingService(float[] result) : IEmbeddingService
    {
        public int CallCount { get; private set; }
        public string? LastText { get; private set; }

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken)
        {
            CallCount++;
            LastText = text;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingSchemaVectorStore(IReadOnlyList<SchemaDocument> documents)
        : ISchemaVectorStore
    {
        public int SearchCallCount { get; private set; }
        public string? LastSearchDataSourceId { get; private set; }
        public IReadOnlyCollection<string>? LastAllowedTableIds { get; private set; }
        public float[]? LastVector { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<SchemaDocument>> SearchAsync(
            string dataSourceId,
            IReadOnlyCollection<string> allowedTableIds,
            float[] embedding,
            int limit,
            CancellationToken cancellationToken)
        {
            SearchCallCount++;
            LastSearchDataSourceId = dataSourceId;
            LastAllowedTableIds = allowedTableIds;
            LastVector = embedding;
            LastLimit = limit;
            return Task.FromResult(documents);
        }

        public Task ReplaceAsync(
            string dataSourceId,
            IReadOnlyList<SchemaVector> schemaVectors,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingChatCompletionService(string result)
        : IChatCompletionService
    {
        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingQueryExecutor(QueryExecutionResult result)
        : IGeneratedQueryExecutor
    {
        public int CallCount { get; private set; }
        public string? LastDataSourceId { get; private set; }
        public ValidatedSql? LastSql { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<QueryExecutionResult> ExecuteAsync(
            string dataSourceId,
            ValidatedSql sql,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDataSourceId = dataSourceId;
            LastSql = sql;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}