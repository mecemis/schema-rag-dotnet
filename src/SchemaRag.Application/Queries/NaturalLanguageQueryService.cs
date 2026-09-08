using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;
using SchemaRag.Application.Prompts;
using SchemaRag.Application.Sql;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Queries;

public sealed class NaturalLanguageQueryService(
    IDataSourceRegistry dataSourceRegistry,
    ISchemaCatalog schemaCatalog,
    IEmbeddingService embeddingService,
    ISchemaVectorStore schemaVectorStore,
    IChatCompletionService chatCompletionService,
    ISemanticQueryCache semanticQueryCache,
    SqlPromptBuilder promptBuilder,
    SqlQueryValidator sqlValidator,
    IGeneratedQueryExecutor queryExecutor,
    QueryPipelineOptions options)
{
    public async Task<NaturalLanguageQueryResult> AskAsync(
        string dataSourceId,
        string question,
        IReadOnlyCollection<string>? tableIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        dataSourceRegistry.GetRequired(dataSourceId);

        var availableSchemas = await schemaCatalog.GetAllAsync(dataSourceId, cancellationToken);
        var allowedSchemas = ResolveAllowedSchemas(
            availableSchemas,
            tableIds,
            options.MaxSelectedTables);
        var allowedTableIds = allowedSchemas.Select(schema => schema.TableId).ToArray();
        var normalizedQuestion = question.Trim();
        var embedding = await embeddingService.GenerateAsync(normalizedQuestion, cancellationToken);
        var schemaFingerprint = SemanticCacheKey.CreateSchemaFingerprint(allowedSchemas);
        var cacheHit = await semanticQueryCache.FindAsync(
            new SemanticCacheLookup(dataSourceId, schemaFingerprint, embedding),
            cancellationToken);
        var reusable = ResolveReusableSql(cacheHit, allowedSchemas);

        if (reusable is not null)
        {
            return await ExecuteAsync(
                dataSourceId,
                normalizedQuestion,
                reusable.Sql,
                reusable.Schemas,
                cacheHit!.Similarity,
                cancellationToken);
        }

        var retrievedSchemas = await schemaVectorStore.SearchAsync(
            dataSourceId,
            allowedTableIds,
            embedding,
            options.SchemaRetrievalCount,
            cancellationToken);
        var allowedIds = allowedTableIds.ToHashSet(StringComparer.Ordinal);
        var schemas = retrievedSchemas
            .Where(schema =>
                string.Equals(schema.DataSourceId, dataSourceId, StringComparison.Ordinal) &&
                allowedIds.Contains(schema.TableId))
            .DistinctBy(schema => schema.TableId)
            .ToArray();
        var prompt = promptBuilder.Build(normalizedQuestion, schemas);
        var generatedSql = await chatCompletionService.GenerateAsync(prompt, cancellationToken);
        var validation = sqlValidator.Validate(generatedSql, schemas);

        if (!validation.IsValid)
        {
            throw new UnsafeSqlException(
                validation.Error ?? "Generated SQL failed validation.");
        }

        var result = await ExecuteAsync(
            dataSourceId,
            normalizedQuestion,
            validation.Sql!,
            schemas,
            cacheSimilarity: null,
            cancellationToken);

        if (IsWorthCaching(validation.Sql!))
        {
            await semanticQueryCache.StoreAsync(
                new SemanticCacheEntry(
                    dataSourceId,
                    schemaFingerprint,
                    normalizedQuestion,
                    embedding,
                    validation.Sql!.Value,
                    schemas.Select(schema => schema.TableId).ToArray()),
                cancellationToken);
        }

        return result;
    }

    private async Task<NaturalLanguageQueryResult> ExecuteAsync(
        string dataSourceId,
        string question,
        ValidatedSql sql,
        IReadOnlyList<SchemaDocument> schemas,
        double? cacheSimilarity,
        CancellationToken cancellationToken)
    {
        var execution = await queryExecutor.ExecuteAsync(dataSourceId, sql, cancellationToken);
        var retrievedTables = schemas
            .Select(schema => schema.QualifiedTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NaturalLanguageQueryResult(
            dataSourceId,
            question,
            retrievedTables,
            sql.Value,
            execution.Columns,
            execution.Rows,
            execution.ExecutionTimeMs,
            cacheSimilarity is not null,
            cacheSimilarity);
    }

    /// <summary>
    /// A cached statement is only reused after it passes the same validation the
    /// model output goes through, against schema documents rehydrated from the
    /// catalog rather than from the cache payload. Anything that no longer holds
    /// is treated as a miss so the model can answer again.
    /// </summary>
    private ReusableSql? ResolveReusableSql(
        SemanticCacheHit? cacheHit,
        IReadOnlyList<SchemaDocument> allowedSchemas)
    {
        if (cacheHit is null || cacheHit.TableIds.Count == 0)
        {
            return null;
        }

        var schemasById = allowedSchemas.ToDictionary(schema => schema.TableId, StringComparer.Ordinal);
        var cachedIds = cacheHit.TableIds.Distinct(StringComparer.Ordinal).ToArray();
        if (cachedIds.Any(tableId => !schemasById.ContainsKey(tableId)))
        {
            return null;
        }

        var schemas = cachedIds.Select(tableId => schemasById[tableId]).ToArray();
        var validation = sqlValidator.Validate(cacheHit.Sql, schemas);

        return validation.IsValid
            ? new ReusableSql(validation.Sql!, schemas)
            : null;
    }

    private static bool IsWorthCaching(ValidatedSql sql) =>
        !string.Equals(sql.Value, SqlPromptBuilder.FallbackSql, StringComparison.Ordinal);

    private static IReadOnlyList<SchemaDocument> ResolveAllowedSchemas(
        IReadOnlyList<SchemaDocument> availableSchemas,
        IReadOnlyCollection<string>? tableIds,
        int maxSelectedTables)
    {
        if (tableIds is null)
        {
            return availableSchemas;
        }

        var selectedIds = tableIds
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            throw new SchemaSelectionException("At least one table must be selected.");
        }


        if (selectedIds.Length > maxSelectedTables)
        {
            throw new SchemaSelectionException(
                $"No more than {maxSelectedTables} tables can be queried at once.");
        }

        var documentsById = availableSchemas.ToDictionary(schema => schema.TableId, StringComparer.Ordinal);
        if (selectedIds.Any(tableId => !documentsById.ContainsKey(tableId)))
        {
            throw new SchemaSelectionException("One or more selected tables are not available for this data source.");
        }

        return selectedIds.Select(tableId => documentsById[tableId]).ToArray();
    }

    private sealed record ReusableSql(ValidatedSql Sql, IReadOnlyList<SchemaDocument> Schemas);
}