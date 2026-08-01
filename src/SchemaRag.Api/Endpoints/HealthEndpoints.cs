namespace SchemaRag.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")))
            .WithName("Health")
            .WithTags("Health")
            .Produces<HealthResponse>();

        return endpoints;
    }

    private sealed record HealthResponse(string Status);
}