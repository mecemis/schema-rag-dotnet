using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Abstractions;

public interface ISchemaCatalog
{
    Task<IReadOnlyList<SchemaDocument>> GetAllAsync(
        string dataSourceId,
        CancellationToken cancellationToken);
}