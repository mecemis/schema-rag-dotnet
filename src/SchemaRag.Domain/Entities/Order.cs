namespace SchemaRag.Domain.Entities;

public sealed class Order
{
    private Order()
    {
    }

    public Order(
        int id,
        int customerId,
        DateOnly orderDate,
        string status,
        decimal totalAmount)
    {
        Id = id;
        CustomerId = customerId;
        OrderDate = orderDate;
        Status = status;
        TotalAmount = totalAmount;
    }

    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
}