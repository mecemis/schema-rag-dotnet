using Microsoft.AspNetCore.Diagnostics;

using Npgsql;

using SchemaRag.Application.DataSources;
using SchemaRag.Application.Indexing;
using SchemaRag.Application.Sql;

namespace SchemaRag.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DataSourceNotFoundException)
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Data source not found",
                    detail: "The requested data source is not configured.")
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is SchemaSelectionException)
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid schema selection",
                    detail: exception.Message)
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is UnsafeSqlException)
        {
            logger.LogWarning("Generated SQL was rejected by the safety validator: {Reason}",
                exception.Message);
            await Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Generated SQL was rejected",
                    detail: "The generated query did not pass the backend safety policy.")
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is PostgresException)
        {
            logger.LogWarning(exception, "Generated SQL could not be executed by PostgreSQL.");
            await Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Generated SQL could not be executed",
                    detail: "PostgreSQL rejected the generated query.")
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is HttpRequestException or NpgsqlException or TimeoutException)
        {
            logger.LogError(exception, "A required dependency was unavailable.");
            await Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Dependency unavailable",
                    detail: "A required local dependency is unavailable. Check PostgreSQL, Qdrant, and Ollama.")
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        logger.LogError(exception, "An unhandled API error occurred.");
        await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error",
                detail: "An unexpected error occurred.")
            .ExecuteAsync(httpContext);
        return true;
    }
}