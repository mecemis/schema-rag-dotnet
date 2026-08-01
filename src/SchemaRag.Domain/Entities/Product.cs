namespace SchemaRag.Domain.Entities;

public sealed class Product
{
    private Product()
    {
    }

    public Product(
        int id,
        int categoryId,
        string name,
        string sku,
        decimal unitPrice,
        int stockQuantity)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Sku = sku;
        UnitPrice = unitPrice;
        StockQuantity = stockQuantity;
    }

    public int Id { get; private set; }
    public int CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int StockQuantity { get; private set; }
}