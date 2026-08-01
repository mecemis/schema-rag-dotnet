namespace SchemaRag.Domain.Entities;

public sealed class Customer
{
    private Customer()
    {
    }

    public Customer(int id, string name, string email, string country, DateTime createdAt)
    {
        Id = id;
        Name = name;
        Email = email;
        Country = country;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
}