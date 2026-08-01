using SchemaRag.Domain.DataSources;

namespace SchemaRag.Application.Abstractions;

public interface IDataSourceRegistry
{
    string DefaultDataSourceId { get; }

    IReadOnlyList<DataSourceDescriptor> GetAll();

    DataSourceDescriptor GetRequired(string dataSourceId);
}