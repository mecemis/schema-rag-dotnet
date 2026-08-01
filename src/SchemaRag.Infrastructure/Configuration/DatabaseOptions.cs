namespace SchemaRag.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string SetupConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 5;
    public int MaxRows { get; init; } = 100;
}