using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using SchemaRag.Application.Abstractions;
using SchemaRag.Infrastructure.Configuration;

namespace SchemaRag.Infrastructure.Ollama;

public sealed class OllamaEmbeddingService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options) : IEmbeddingService
{
    public async Task<float[]> GenerateAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var response = await httpClient.PostAsJsonAsync(
            "api/embed",
            new OllamaEmbedRequest(options.Value.EmbeddingModel, text),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
            cancellationToken);
        var embedding = result?.Embeddings.FirstOrDefault();
        if (embedding is null || embedding.Length == 0)
        {
            throw new InvalidOperationException("Ollama returned an empty embedding.");
        }

        if (embedding.Length != options.Value.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Ollama returned {embedding.Length} embedding dimensions; " +
                $"{options.Value.EmbeddingDimensions} were configured.");
        }

        return embedding;
    }

    private sealed record OllamaEmbedRequest(string Model, string Input);

    private sealed record OllamaEmbedResponse(IReadOnlyList<float[]> Embeddings);
}