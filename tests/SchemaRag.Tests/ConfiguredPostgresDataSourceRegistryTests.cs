using Microsoft.Extensions.Options;

using SchemaRag.Application.DataSources;
using SchemaRag.Infrastructure.Configuration;
using SchemaRag.Infrastructure.DataSources;

namespace SchemaRag.Tests;

public sealed class ConfiguredPostgresDataSourceRegistryTests
{
    [Fact]
    public async Task GetAll_ReturnsOnlySafeDescriptorsAndKeepsSourcesDistinct()
    {
        await using var registry = CreateRegistry();

        var descriptors = registry.GetAll();

        Assert.Equal(2, descriptors.Count);
        Assert.Equal("sales", descriptors[0].Id);
        Assert.True(descriptors[0].IsDefault);
        Assert.DoesNotContain(
            descriptors.Select(descriptor => descriptor.ToString()),
            value => value.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("warehouse", registry.GetRequired("warehouse").Id);
        Assert.Equal(["reporting"], registry.GetIncludedSchemas("warehouse"));
    }

    [Fact]
    public async Task GetRequired_WhenSourceIsUnknown_ThrowsWithoutFallback()
    {
        await using var registry = CreateRegistry();

        Assert.Throws<DataSourceNotFoundException>(() => registry.GetRequired("missing"));
    }

    private static ConfiguredPostgresDataSourceRegistry CreateRegistry()
    {
        var options = new DataSourcesOptions
        {
            DefaultId = "sales",
            Profiles = new Dictionary<string, PostgresDataSourceOptions>
            {
                ["sales"] = new()
                {
                    DisplayName = "Sales",
                    ReadOnlyConnectionString =
                        "Host=sales.test;Database=sales;Username=sales_reader;Password=test-only-placeholder",
                    IncludedSchemas = ["public"]
                },
                ["warehouse"] = new()
                {
                    DisplayName = "Warehouse",
                    ReadOnlyConnectionString =
                        "Host=warehouse.test;Database=warehouse;Username=warehouse_reader;Password=second-test-placeholder",
                    IncludedSchemas = ["reporting"]
                }
            }
        };

        return new ConfiguredPostgresDataSourceRegistry(
            Options.Create(options),
            Options.Create(new DatabaseOptions()));
    }
}