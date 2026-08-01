using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class SchemaTableIdentityTests
{
    [Fact]
    public void CreateId_IsStableOpaqueAndSourceScoped()
    {
        var first = SchemaTableIdentity.CreateId("sales", "public", "customers");
        var repeated = SchemaTableIdentity.CreateId("sales", "public", "customers");
        var otherSource = SchemaTableIdentity.CreateId("warehouse", "public", "customers");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherSource);
        Assert.StartsWith("tbl_", first, StringComparison.Ordinal);
        Assert.DoesNotContain("customers", first, StringComparison.Ordinal);
    }
}