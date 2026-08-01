namespace SchemaRag.Domain.DataSources;

public sealed record DataSourceDescriptor(
    string Id,
    string DisplayName,
    bool IsDefault);