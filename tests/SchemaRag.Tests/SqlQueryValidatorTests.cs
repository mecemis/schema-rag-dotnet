using SchemaRag.Application.Sql;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Tests;

public sealed class SqlQueryValidatorTests
{
    private readonly SqlQueryValidator _validator = new(maxRows: 100);

    [Theory]
    [InlineData("SELECT id, name FROM customers LIMIT 5")]
    [InlineData("  select id from products limit 10;  ")]
    public void Validate_Select_IsAccepted(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.True(result.IsValid, result.Error);
        Assert.NotNull(result.Sql);
    }

    [Fact]
    public void Validate_CteEndingInSelect_IsAccepted()
    {
        const string sql = "WITH totals AS (SELECT customer_id, SUM(total_amount) AS total FROM orders GROUP BY customer_id) SELECT * FROM totals LIMIT 20";

        var result = _validator.Validate(sql);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(sql, result.Sql!.Value);
    }

    [Theory]
    [InlineData("INSERT INTO customers (name) VALUES ('x')")]
    [InlineData("UPDATE customers SET name = 'x'")]
    [InlineData("DELETE FROM customers")]
    [InlineData("DROP TABLE customers")]
    [InlineData("ALTER TABLE customers ADD COLUMN x int")]
    [InlineData("CREATE TABLE x (id int)")]
    [InlineData("TRUNCATE TABLE customers")]
    [InlineData("MERGE INTO customers USING source ON true WHEN MATCHED THEN DELETE")]
    [InlineData("GRANT SELECT ON customers TO public")]
    [InlineData("REVOKE SELECT ON customers FROM public")]
    [InlineData("COPY customers TO STDOUT")]
    [InlineData("CALL refresh_report()")]
    [InlineData("DO $$ BEGIN RAISE NOTICE 'x'; END $$")]
    [InlineData("WITH changed AS (DELETE FROM customers RETURNING *) SELECT * FROM changed")]
    [InlineData("SELECT * INTO archived_customers FROM customers")]
    [InlineData("SELECT * FROM customers FOR UPDATE")]
    [InlineData("SELECT * FROM customers FOR SHARE")]
    [InlineData("SELECT * FROM customers FOR KEY SHARE")]
    [InlineData("SELECT * FROM customers FOR NO KEY UPDATE")]
    [InlineData("LOCK TABLE customers")]
    public void Validate_DangerousSql_IsRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
        Assert.Null(result.Sql);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1; DELETE FROM customers;")]
    public void Validate_MultipleStatements_AreRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT * FROM customers -- all customers")]
    [InlineData("SELECT * FROM /* hidden */ customers")]
    [InlineData("SELECT/**/1")]
    public void Validate_Comments_AreRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT * FROM pg_catalog.pg_tables")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT * FROM PG_TOAST.pg_class")]
    [InlineData("SELECT * FROM \"pg_catalog\".pg_tables")]
    public void Validate_SystemSchemas_AreRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MissingLimit_AppendsConfiguredMaximum()
    {
        var result = _validator.Validate("SELECT id FROM customers");

        Assert.Equal("SELECT id FROM customers LIMIT 100", result.Sql!.Value);
    }

    [Fact]
    public void Validate_LimitBelowMaximum_IsPreserved()
    {
        var result = _validator.Validate("SELECT id FROM customers LIMIT 5");

        Assert.Equal("SELECT id FROM customers LIMIT 5", result.Sql!.Value);
    }

    [Fact]
    public void Validate_LimitAboveMaximum_IsClamped()
    {
        var result = _validator.Validate("SELECT id FROM customers LIMIT 500 OFFSET 10");

        Assert.Equal("SELECT id FROM customers LIMIT 100 OFFSET 10", result.Sql!.Value);
    }

    [Fact]
    public void Validate_NestedLimit_DoesNotCountAsOuterLimit()
    {
        const string sql = "WITH recent AS (SELECT * FROM orders LIMIT 5) SELECT * FROM recent";

        var result = _validator.Validate(sql);

        Assert.Equal($"{sql} LIMIT 100", result.Sql!.Value);
    }

    [Theory]
    [InlineData("SELECT * FROM customers LIMIT ALL")]
    [InlineData("SELECT * FROM customers FETCH FIRST 20 ROWS ONLY")]
    [InlineData("SELECT * FROM customers LIMIT -5")]
    [InlineData("SELECT * FROM customers LIMIT 5 OFFSET -1")]
    [InlineData("SELECT * FROM customers OFFSET -1")]
    public void Validate_UnsupportedRowLimit_IsRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT pg_sleep(10)")]
    [InlineData("SELECT set_config('statement_timeout', '0', false)")]
    [InlineData("SELECT pg_advisory_lock(42)")]
    [InlineData("SELECT \"pg_sleep\"(10)")]
    [InlineData("SELECT \"set_config\"('statement_timeout', '0', false)")]
    [InlineData("SELECT \"pg_advisory_lock\"(42)")]
    public void Validate_DangerousFunction_IsRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ReportingFunctions_AreAccepted()
    {
        const string sql = "SELECT DATE_TRUNC('month', order_date), SUM(total_amount) FROM orders GROUP BY 1 LIMIT 12";

        var result = _validator.Validate(sql);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_WithAllowedSchemas_AcceptsOnlyRetrievedTables()
    {
        var allowedSchemas = new[]
        {
            CreateSchema("customers"),
            CreateSchema("orders")
        };

        var result = _validator.Validate(
            "SELECT c.name FROM public.customers c JOIN public.orders o ON o.customer_id = c.id LIMIT 5",
            allowedSchemas);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_WithAllowedSchemas_RejectsTableOutsideRetrievedContext()
    {
        var result = _validator.Validate(
            "SELECT name FROM public.products LIMIT 5",
            [CreateSchema("customers")]);

        Assert.False(result.IsValid);
        Assert.Contains("not part of the retrieved schema context", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WithAllowedSchemas_RejectsUnqualifiedTableReference()
    {
        var result = _validator.Validate(
            "SELECT name FROM customers LIMIT 5",
            [CreateSchema("customers")]);

        Assert.False(result.IsValid);
        Assert.Contains("schema-qualified", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WithAllowedSchemas_AllowsCteNamesButStillChecksCteSources()
    {
        var result = _validator.Validate(
            "WITH totals AS (SELECT customer_id, SUM(total_amount) total FROM public.orders GROUP BY customer_id) SELECT * FROM totals LIMIT 5",
            [CreateSchema("orders")]);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_WithAllowedSchemas_RejectsCommaSeparatedTableBypass()
    {
        var result = _validator.Validate(
            "SELECT * FROM public.customers c, public.products p LIMIT 5",
            [CreateSchema("customers")]);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("EXPLAIN SELECT * FROM customers")]
    [InlineData("```sql\nSELECT * FROM customers\n```")]
    public void Validate_InvalidShape_IsRejected(string sql)
    {
        var result = _validator.Validate(sql);

        Assert.False(result.IsValid);
    }

    private static SchemaDocument CreateSchema(string tableName) =>
        new(
            "sales",
            "public",
            tableName,
            $"The {tableName} table.",
            [new SchemaColumn("id", "integer", "Primary key.", false, true)],
            []);
}