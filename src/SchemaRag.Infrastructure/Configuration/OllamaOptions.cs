namespace SchemaRag.Infrastructure.Configuration;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string ChatModel { get; init; } = "qwen2.5-coder:7b";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public int EmbeddingDimensions { get; init; } = 768;
    public int RequestTimeoutSeconds { get; init; } = 120;
}