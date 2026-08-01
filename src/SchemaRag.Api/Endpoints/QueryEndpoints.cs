using Microsoft.Extensions.Options;

using SchemaRag.Api.Configuration;
using SchemaRag.Application.Queries;

namespace SchemaRag.Api.Endpoints;

public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/query", HandleAsync)
            .WithName("Query")
            .WithTags("Query")
            .Accepts<QueryRequest>("application/json")
            .Produces<NaturalLanguageQueryResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        QueryRequest? request,
        NaturalLanguageQueryService queryService,
        SchemaRag.Application.Abstractions.IDataSourceRegistry dataSourceRegistry,
        IOptions<RagOptions> options,
        ILogger<NaturalLanguageQueryService> logger,
        CancellationToken cancellationToken)
    {
        var validationProblem = Validate(request, options.Value.MaxQuestionLength);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var result = await queryService.AskAsync(
            string.IsNullOrWhiteSpace(request!.DataSourceId)
                ? dataSourceRegistry.DefaultDataSourceId
                : request.DataSourceId,
            request!.Question!,
            request.TableIds,
            cancellationToken);
        logger.LogInformation(
            "Natural-language query completed for {DataSourceId} using {SchemaCount} schemas in {ExecutionTimeMs} ms.",
            result.DataSourceId,
            result.RetrievedTables.Count,
            result.ExecutionTimeMs);
        return Results.Ok(result);
    }

    private static IResult? Validate(QueryRequest? request, int maxQuestionLength)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = ["A question is required."]
            });
        }

        if (request.Question.Length > maxQuestionLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = [$"The question must not exceed {maxQuestionLength} characters."]
            });
        }

        return null;
    }

    public sealed record QueryRequest(
        string? Question,
        string? DataSourceId = null,
        IReadOnlyList<string>? TableIds = null);
}