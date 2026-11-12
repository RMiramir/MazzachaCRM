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
    public DateTime CreatedAt { get; private set; }

    public static Customer Create(string name, string? phone, string? note)
    {
        var customer = new Customer { CreatedAt = DateTime.UtcNow };
        customer.Update(name, phone, note);
        return customer;
    }

    public void Update(string name, string? phone, string? note)
    {
        Name = Guard.NotEmpty(name, "Имя клиента");
        Phone = Guard.Optional(phone);
        Note = Guard.Optional(note);
    }
}

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(Customer customer);
    void Remove(Customer customer);
}
