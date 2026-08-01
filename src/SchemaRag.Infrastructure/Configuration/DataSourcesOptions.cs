namespace SchemaRag.Infrastructure.Configuration;

public sealed class DataSourcesOptions
{
    public const string SectionName = "DataSources";

    public string DefaultId { get; init; } = "demo";
    public Dictionary<string, PostgresDataSourceOptions> Profiles { get; init; } = [];
}

public sealed class PostgresDataSourceOptions
{
    public string DisplayName { get; init; } = string.Empty;
    public string ReadOnlyConnectionString { get; init; } = string.Empty;
    public string[] IncludedSchemas { get; init; } = [];
}