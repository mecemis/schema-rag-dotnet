namespace SchemaRag.Application.Abstractions;

public interface IChatCompletionService
{
    Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken);
}