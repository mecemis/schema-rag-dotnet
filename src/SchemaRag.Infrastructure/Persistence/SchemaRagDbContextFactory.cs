using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SchemaRag.Infrastructure.Persistence;

public sealed class SchemaRagDbContextFactory
    : IDesignTimeDbContextFactory<SchemaRagDbContext>
{
    public SchemaRagDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "Database__SetupConnectionString") ?? "Host=localhost;Database=schema_rag";
        var options = new DbContextOptionsBuilder<SchemaRagDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SchemaRagDbContext(options);
    }
}