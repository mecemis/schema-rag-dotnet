using Microsoft.EntityFrameworkCore;

namespace SchemaRag.Infrastructure.Persistence;

public sealed class DatabaseInitializer(SchemaRagDbContext dbContext)
{
    private const string SchemaCommentsSql = """
        COMMENT ON TABLE customers IS 'Customer accounts that place orders in the store.';
        COMMENT ON COLUMN customers.id IS 'Primary key for the customer.';
        COMMENT ON COLUMN customers.name IS 'Customer display name.';
        COMMENT ON COLUMN customers.email IS 'Unique customer email address.';
        COMMENT ON COLUMN customers.country IS 'Customer country.';
        COMMENT ON COLUMN customers.created_at IS 'When the account was created.';

        COMMENT ON TABLE categories IS 'Product groupings used to organize the catalog.';
        COMMENT ON COLUMN categories.id IS 'Primary key for the category.';
        COMMENT ON COLUMN categories.name IS 'Unique category name.';
        COMMENT ON COLUMN categories.description IS 'Business description of the category.';

        COMMENT ON TABLE products IS 'Products available for sale, including current price and stock level.';
        COMMENT ON COLUMN products.id IS 'Primary key for the product.';
        COMMENT ON COLUMN products.category_id IS 'Category that contains the product.';
        COMMENT ON COLUMN products.name IS 'Product display name.';
        COMMENT ON COLUMN products.sku IS 'Unique stock keeping unit.';
        COMMENT ON COLUMN products.unit_price IS 'Current catalog price per unit.';
        COMMENT ON COLUMN products.stock_quantity IS 'Units currently in stock.';

        COMMENT ON TABLE orders IS 'Customer purchases. Total amount is the persisted order total for sales reporting.';
        COMMENT ON COLUMN orders.id IS 'Primary key for the order.';
        COMMENT ON COLUMN orders.customer_id IS 'Customer who placed the order.';
        COMMENT ON COLUMN orders.order_date IS 'Calendar date when the order was placed.';
        COMMENT ON COLUMN orders.status IS 'Order lifecycle status.';
        COMMENT ON COLUMN orders.total_amount IS 'Total monetary amount of the order.';

        COMMENT ON TABLE order_items IS 'Line items that record products, quantities, and prices captured at purchase time.';
        COMMENT ON COLUMN order_items.id IS 'Primary key for the line item.';
        COMMENT ON COLUMN order_items.order_id IS 'Order containing this line item.';
        COMMENT ON COLUMN order_items.product_id IS 'Product purchased on this line.';
        COMMENT ON COLUMN order_items.quantity IS 'Number of units purchased.';
        COMMENT ON COLUMN order_items.unit_price IS 'Per-unit price captured at purchase time.';
        """;

    private const string ReadOnlyGrantsSql = """
        DO $revoke_temp$
        BEGIN
            EXECUTE format(
                'REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC',
                current_database());
        END
        $revoke_temp$;
        REVOKE CREATE ON SCHEMA public FROM PUBLIC;
        GRANT USAGE ON SCHEMA public TO schema_rag_reader;
        REVOKE SELECT ON ALL TABLES IN SCHEMA public FROM schema_rag_reader;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            REVOKE SELECT ON TABLES FROM schema_rag_reader;
        GRANT SELECT ON TABLE
            customers,
            categories,
            products,
            orders,
            order_items
        TO schema_rag_reader;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SchemaCommentsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(ReadOnlyGrantsSql, cancellationToken);
    }
}