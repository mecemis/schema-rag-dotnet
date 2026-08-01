using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;

namespace SchemaRag.Api.Endpoints;

public static class SchemaEndpoints
{
    public static IEndpointRouteBuilder MapSchemaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/schema").WithTags("Schema");

        group.MapGet("/", async (
                IDataSourceRegistry dataSourceRegistry,
                ISchemaCatalog catalog,
                CancellationToken cancellationToken) =>
            {
                var documents = await catalog.GetAllAsync(
                    dataSourceRegistry.DefaultDataSourceId,
                    cancellationToken);
                return Results.Ok(documents);
            })
            .WithName("GetSchema")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/index", async (
                IDataSourceRegistry dataSourceRegistry,
                ISchemaCatalog catalog,
                SchemaIndexingService indexingService,
                ILogger<SchemaIndexingService> logger,
                CancellationToken cancellationToken) =>
            {
                var dataSourceId = dataSourceRegistry.DefaultDataSourceId;
                var documents = await catalog.GetAllAsync(dataSourceId, cancellationToken);
                var result = await indexingService.IndexAsync(
                    dataSourceId,
                    documents.Select(document => document.TableId).ToArray(),
                    cancellationToken);
                logger.LogInformation("Indexed {SchemaCount} schema documents.", result.IndexedCount);
                return Results.Ok(result);
            })
            .WithName("IndexSchema")
            .RequireRateLimiting("schema-index")
            .Produces<SchemaIndexResult>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}