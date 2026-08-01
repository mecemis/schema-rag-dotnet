namespace SchemaRag.Application.DataSources;

public sealed class DataSourceNotFoundException(string dataSourceId)
    : KeyNotFoundException($"Data source '{dataSourceId}' is not configured.")
{
    public string DataSourceId { get; } = dataSourceId;
}