using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
        {
            table.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            table.HasCheckConstraint("ck_order_items_unit_price", "unit_price >= 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OrderId).HasColumnName("order_id");
        builder.Property(item => item.ProductId).HasColumnName("product_id");
        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(12, 2);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => item.OrderId);
        builder.HasIndex(item => item.ProductId);

        builder.HasData(
            new OrderItem(1, 1, 1, 1, 49.90m),
            new OrderItem(2, 1, 2, 1, 89.00m),
            new OrderItem(3, 2, 3, 2, 39.50m),
            new OrderItem(4, 3, 4, 1, 199.00m),
            new OrderItem(5, 4, 8, 2, 59.00m),
            new OrderItem(6, 5, 5, 1, 79.00m),
            new OrderItem(7, 6, 7, 2, 45.00m),
            new OrderItem(8, 7, 8, 1, 59.00m),
            new OrderItem(9, 7, 6, 3, 6.50m),
            new OrderItem(10, 8, 2, 2, 89.00m),
            new OrderItem(11, 9, 1, 2, 49.90m),
            new OrderItem(12, 10, 3, 3, 39.50m),
            new OrderItem(13, 11, 4, 1, 199.00m),
            new OrderItem(14, 11, 8, 1, 59.00m),
            new OrderItem(15, 12, 7, 3, 45.00m));
    }
}