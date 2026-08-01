using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("ck_products_unit_price", "unit_price >= 0");
            table.HasCheckConstraint("ck_products_stock_quantity", "stock_quantity >= 0");
        });
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).HasColumnName("id");
        builder.Property(product => product.CategoryId).HasColumnName("category_id");
        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(product => product.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(product => product.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(12, 2);
        builder.Property(product => product.StockQuantity).HasColumnName("stock_quantity");
        builder.HasIndex(product => product.Sku).IsUnique();
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new Product(1, 1, "Laptop Stand", "OFF-STAND-01", 49.90m, 24),
            new Product(2, 2, "Mechanical Keyboard", "ELEC-KEY-01", 89.00m, 12),
            new Product(3, 2, "Wireless Mouse", "ELEC-MOUSE-01", 39.50m, 18),
            new Product(4, 2, "Noise-Cancelling Headphones", "ELEC-AUDIO-01", 199.00m, 4),
            new Product(5, 2, "Webcam", "ELEC-CAM-01", 79.00m, 6),
            new Product(6, 3, "Hardcover Notebook", "STAT-NOTE-01", 6.50m, 40),
            new Product(7, 1, "Desk Lamp", "OFF-LAMP-01", 45.00m, 9),
            new Product(8, 2, "USB-C Hub", "ELEC-HUB-01", 59.00m, 7));
    }
}