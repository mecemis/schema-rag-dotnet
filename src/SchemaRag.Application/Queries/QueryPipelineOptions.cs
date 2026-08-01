namespace SchemaRag.Application.Queries;

public sealed record QueryPipelineOptions
{
    public QueryPipelineOptions(int schemaRetrievalCount, int maxSelectedTables = 100)
    {
        if (schemaRetrievalCount is < 3 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaRetrievalCount),
                "Schema retrieval count must be between 3 and 5.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxSelectedTables, 1);

        SchemaRetrievalCount = schemaRetrievalCount;
        MaxSelectedTables = maxSelectedTables;
    }

    public int SchemaRetrievalCount { get; }
    public int MaxSelectedTables { get; }
}