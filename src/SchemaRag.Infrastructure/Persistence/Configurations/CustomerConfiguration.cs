using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasColumnName("id");
        builder.Property(customer => customer.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(customer => customer.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(customer => customer.Country)
            .HasColumnName("country")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(customer => customer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(customer => customer.Email).IsUnique();

        builder.HasData(
            new Customer(1, "Ada Lovelace", "ada@example.test", "United Kingdom", Utc(2024, 1, 10)),
            new Customer(2, "Grace Hopper", "grace@example.test", "United States", Utc(2024, 2, 5)),
            new Customer(3, "Edsger Dijkstra", "edsger@example.test", "Netherlands", Utc(2024, 3, 12)),
            new Customer(4, "Margaret Hamilton", "margaret@example.test", "United States", Utc(2024, 4, 18)),
            new Customer(5, "Alan Turing", "alan@example.test", "United Kingdom", Utc(2024, 5, 9)));
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);
}