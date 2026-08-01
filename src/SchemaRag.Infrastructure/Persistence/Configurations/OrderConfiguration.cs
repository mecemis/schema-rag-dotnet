using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", table =>
            table.HasCheckConstraint("ck_orders_total_amount", "total_amount >= 0"));
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id");
        builder.Property(order => order.CustomerId).HasColumnName("customer_id");
        builder.Property(order => order.OrderDate)
            .HasColumnName("order_date")
            .HasColumnType("date");
        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(order => order.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(12, 2);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(order => order.CustomerId);
        builder.HasIndex(order => order.OrderDate);

        builder.HasData(
            new Order(1, 1, new DateOnly(2025, 1, 10), "Completed", 138.90m),
            new Order(2, 2, new DateOnly(2025, 1, 22), "Completed", 79.00m),
            new Order(3, 1, new DateOnly(2025, 2, 15), "Completed", 199.00m),
            new Order(4, 3, new DateOnly(2025, 2, 21), "Completed", 118.00m),
            new Order(5, 4, new DateOnly(2025, 3, 3), "Completed", 79.00m),
            new Order(6, 5, new DateOnly(2025, 3, 18), "Completed", 90.00m),
            new Order(7, 2, new DateOnly(2025, 4, 5), "Completed", 78.50m),
            new Order(8, 3, new DateOnly(2025, 4, 27), "Completed", 178.00m),
            new Order(9, 4, new DateOnly(2025, 5, 11), "Completed", 99.80m),
            new Order(10, 1, new DateOnly(2025, 5, 23), "Completed", 118.50m),
            new Order(11, 5, new DateOnly(2025, 6, 2), "Completed", 258.00m),
            new Order(12, 2, new DateOnly(2025, 6, 19), "Completed", 135.00m));
    }
}