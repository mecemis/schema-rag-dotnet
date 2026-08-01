using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(category => category.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();
        builder.HasIndex(category => category.Name).IsUnique();

        builder.HasData(
            new Category(1, "Office", "Desk and office accessories."),
            new Category(2, "Electronics", "Computer and audio electronics."),
            new Category(3, "Stationery", "Paper goods and writing supplies."));
    }
}