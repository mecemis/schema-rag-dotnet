using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Infrastructure.Configuration;

namespace SchemaRag.Infrastructure.Ollama;

public sealed class OllamaChatCompletionService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options) : IChatCompletionService
{
    public async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var response = await httpClient.PostAsJsonAsync(
            "api/generate",
            new OllamaGenerateRequest(
                options.Value.ChatModel,
                prompt,
                false,
                new OllamaGenerateOptions(0)),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(result?.Response))
        {
            throw new InvalidOperationException("Ollama returned an empty chat response.");
        }

        return result.Response.Trim();
    }

    private sealed record OllamaGenerateRequest(
        string Model,
        string Prompt,
        bool Stream,
        OllamaGenerateOptions Options);

    private sealed record OllamaGenerateOptions(double Temperature);

    private sealed record OllamaGenerateResponse(string Response);
}