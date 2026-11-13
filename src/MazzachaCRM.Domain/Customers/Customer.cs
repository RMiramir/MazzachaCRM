using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Customers;

public sealed class Customer : AggregateRoot
{
    private Customer()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Магазин, который платит после продажи и может вернуть непроданное.</summary>
    public bool IsConsignment { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Customer Create(string name, string? phone, string? note, bool isConsignment = false)
    {
        var customer = new Customer { CreatedAt = DateTime.UtcNow };
        customer.Update(name, phone, note, isConsignment);
        return customer;
    }

    public void Update(string name, string? phone, string? note, bool isConsignment)
    {
        Name = Guard.NotEmpty(name, "Имя клиента");
        Phone = Guard.Optional(phone);
        Note = Guard.Optional(note);
        IsConsignment = isConsignment;
    }
}

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(Customer customer);
    void Remove(Customer customer);
}
