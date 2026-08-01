namespace SchemaRag.Domain.Entities;

public sealed class Category
{
    private Category()
    {
    }

    public Category(int id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
}