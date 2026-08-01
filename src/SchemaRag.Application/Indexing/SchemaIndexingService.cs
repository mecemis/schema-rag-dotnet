using SchemaRag.Application.Abstractions;

namespace SchemaRag.Application.Indexing;

public sealed class SchemaIndexingService(
    IDataSourceRegistry dataSourceRegistry,
    ISchemaCatalog schemaCatalog,
    IEmbeddingService embeddingService,
    ISchemaVectorStore schemaVectorStore,
    SchemaIndexingOptions options)
{
    public async Task<SchemaIndexResult> IndexAsync(
        string dataSourceId,
        IReadOnlyCollection<string> tableIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(tableIds);
        dataSourceRegistry.GetRequired(dataSourceId);

        var selectedIds = tableIds
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            throw new SchemaSelectionException("At least one table must be selected.");
        }

        if (selectedIds.Length > options.MaxTablesPerIndex)
        {
            throw new SchemaSelectionException(
                $"No more than {options.MaxTablesPerIndex} tables can be indexed at once.");
        }

        var documents = await schemaCatalog.GetAllAsync(dataSourceId, cancellationToken);
        var documentsById = documents.ToDictionary(document => document.TableId, StringComparer.Ordinal);
        var unknownIds = selectedIds.Where(tableId => !documentsById.ContainsKey(tableId)).ToArray();
        if (unknownIds.Length > 0)
        {
            throw new SchemaSelectionException("One or more selected tables are not available for this data source.");
        }

        var selectedDocuments = selectedIds.Select(tableId => documentsById[tableId]).ToArray();
        var vectors = await GenerateVectorsAsync(selectedDocuments, cancellationToken);
        await schemaVectorStore.ReplaceAsync(dataSourceId, vectors, cancellationToken);

        return new SchemaIndexResult(
            dataSourceId,
            vectors.Length,
            selectedDocuments.Select(document => document.QualifiedTableName).ToArray());
    }

    private async Task<SchemaVector[]> GenerateVectorsAsync(
        IReadOnlyList<Domain.Schema.SchemaDocument> documents,
        CancellationToken cancellationToken)
    {
        using var concurrencyGate = new SemaphoreSlim(options.EmbeddingConcurrency);
        var vectorTasks = documents.Select(async document =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var text = SchemaTextFormatter.Format(document);
                var embedding = await embeddingService.GenerateAsync(text, cancellationToken);
                return new SchemaVector(
                    document,
                    embedding,
                    SchemaDocumentVersion.Create(document));
            }
            finally
            {
                concurrencyGate.Release();
            }
        });

        return await Task.WhenAll(vectorTasks);
    }
}

public sealed record SchemaIndexResult(
    string DataSourceId,
    int IndexedCount,
    IReadOnlyList<string> Tables);