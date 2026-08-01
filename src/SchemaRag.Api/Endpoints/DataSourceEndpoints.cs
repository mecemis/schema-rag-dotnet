using SchemaRag.Application.Abstractions;
using SchemaRag.Application.Indexing;

namespace SchemaRag.Api.Endpoints;

public static class DataSourceEndpoints
{
    public static IEndpointRouteBuilder MapDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/data-sources").WithTags("Data sources");

        group.MapGet("/", (IDataSourceRegistry registry) => Results.Ok(registry.GetAll()))
            .WithName("GetDataSources")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/{dataSourceId}/schema", async (
                string dataSourceId,
                IDataSourceRegistry registry,
                ISchemaCatalog catalog,
                CancellationToken cancellationToken) =>
            {
                registry.GetRequired(dataSourceId);
                var documents = await catalog.GetAllAsync(dataSourceId, cancellationToken);
                return Results.Ok(documents);
            })
            .WithName("DiscoverDataSourceSchema")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{dataSourceId}/schema/index", async (
                string dataSourceId,
                IndexSchemaRequest? request,
                SchemaIndexingService indexingService,
                ILogger<SchemaIndexingService> logger,
                CancellationToken cancellationToken) =>
            {
                if (request?.TableIds is null || request.TableIds.Count == 0)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["tableIds"] = ["Select at least one discovered table."]
                    });
                }

                var result = await indexingService.IndexAsync(
                    dataSourceId,
                    request.TableIds,
                    cancellationToken);
                logger.LogInformation(
                    "Indexed {SchemaCount} schema documents for data source {DataSourceId}.",
                    result.IndexedCount,
                    dataSourceId);
                return Results.Ok(result);
            })
            .WithName("IndexDataSourceSchema")
            .RequireRateLimiting("schema-index")
            .Accepts<IndexSchemaRequest>("application/json")
            .Produces<SchemaIndexResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    public sealed record IndexSchemaRequest(IReadOnlyList<string>? TableIds);
}