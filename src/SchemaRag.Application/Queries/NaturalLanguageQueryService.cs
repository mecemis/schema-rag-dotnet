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

        var execution = await queryExecutor.ExecuteAsync(
            dataSourceId,
            validation.Sql!,
            cancellationToken);
        var retrievedTables = schemas
            .Select(schema => schema.QualifiedTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NaturalLanguageQueryResult(
            dataSourceId,
            normalizedQuestion,
            retrievedTables,
            validation.Sql!.Value,
            execution.Columns,
            execution.Rows,
            execution.ExecutionTimeMs);
    }

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
}