using SchemaRag.Application.Queries;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class SemanticCacheKeyTests
{
    [Fact]
    public void CreateSchemaFingerprint_IsIndependentOfTheOrderTablesArriveIn()
    {
        var customers = CreateSchema("customers", "text");
        var orders = CreateSchema("orders", "text");

        Assert.Equal(
            SemanticCacheKey.CreateSchemaFingerprint([customers, orders]),
            SemanticCacheKey.CreateSchemaFingerprint([orders, customers]));
    }

    [Fact]
    public void CreateSchemaFingerprint_ChangesWhenAColumnTypeChanges()
    {
        var before = SemanticCacheKey.CreateSchemaFingerprint([CreateSchema("customers", "text")]);
        var after = SemanticCacheKey.CreateSchemaFingerprint([CreateSchema("customers", "integer")]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void CreateSchemaFingerprint_ChangesWhenTheAllowListChanges()
    {
        var customers = CreateSchema("customers", "text");
        var orders = CreateSchema("orders", "text");

        Assert.NotEqual(
            SemanticCacheKey.CreateSchemaFingerprint([customers]),
            SemanticCacheKey.CreateSchemaFingerprint([customers, orders]));
    }

    [Theory]
    [InlineData("List customer names", "list customer names")]
    [InlineData("  List   Customer   Names  ", "list customer names")]
    [InlineData("List\tcustomer\nnames", "list customer names")]
    public void NormalizeQuestion_CollapsesCasingAndWhitespace(string question, string expected) =>
        Assert.Equal(expected, SemanticCacheKey.NormalizeQuestion(question));

    [Fact]
    public void NormalizeQuestion_RejectsAnEmptyQuestion() =>
        Assert.Throws<ArgumentException>(() => SemanticCacheKey.NormalizeQuestion("   "));

    private static SchemaDocument CreateSchema(string tableName, string columnType) =>
        new(
            "sales",
            "public",
            tableName,
            $"{tableName} business data.",
            [new SchemaColumn("name", columnType, "Display name.", false)],
            []);
}