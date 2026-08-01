using Microsoft.EntityFrameworkCore;

using SchemaRag.Domain.Entities;

namespace SchemaRag.Infrastructure.Persistence;

public sealed class SchemaRagDbContext(DbContextOptions<SchemaRagDbContext> options)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchemaRagDbContext).Assembly);
    }
}