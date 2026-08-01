using SchemaRag.Application.Prompts;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class SqlPromptBuilderTests
{
    [Fact]
    public void Build_IncludesQuestionRetrievedSchemaAndSafetyInstructions()
    {
        var schema = new SchemaDocument(
            "demo",
            "public",
            "customers",
            "People who place orders.",
            [
                new SchemaColumn("id", "integer", "Primary key.", false),
                new SchemaColumn("name", "text", "Customer display name.", false)
            ],
            [new SchemaRelationship("id", "public", "orders", "customer_id", "One customer has many orders.")]);
        var builder = new SqlPromptBuilder(maxRows: 100);

        var prompt = builder.Build("Show customer names.", [schema]);

        Assert.Contains("Show customer names.", prompt, StringComparison.Ordinal);
        Assert.Contains("customers", prompt, StringComparison.Ordinal);
        Assert.Contains("public.customers", prompt, StringComparison.Ordinal);
        Assert.Contains("People who place orders.", prompt, StringComparison.Ordinal);
        Assert.Contains("name (text, not null): Customer display name.", prompt, StringComparison.Ordinal);
        Assert.Contains("orders.customer_id", prompt, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", prompt, StringComparison.Ordinal);
        Assert.Contains("Return only SQL", prompt, StringComparison.Ordinal);
        Assert.Contains("only the supplied schema", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never invent tables or columns", prompt, StringComparison.Ordinal);
        Assert.Contains("exactly one read-only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit JOIN", prompt, StringComparison.Ordinal);
        Assert.Contains("LIMIT no greater than 100", prompt, StringComparison.Ordinal);
        Assert.Contains(SqlPromptBuilder.FallbackSql, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("unretrieved_table", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithNoSchemas_StillProvidesKnownFallback()
    {
        var builder = new SqlPromptBuilder(maxRows: 50);

        var prompt = builder.Build("Unknown question", []);

        Assert.Contains("No schema documents were retrieved.", prompt, StringComparison.Ordinal);
        Assert.Contains(SqlPromptBuilder.FallbackSql, prompt, StringComparison.Ordinal);
    }
}