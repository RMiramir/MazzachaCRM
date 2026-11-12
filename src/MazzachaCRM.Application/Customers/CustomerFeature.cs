using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Customers;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Note,
    int OrdersCount,
    decimal OrdersTotal,
    DateOnly? LastOrderDate);

// ---------- Список ----------

public sealed record GetCustomersQuery(string? Search) : IQuery<IReadOnlyList<CustomerDto>>;

public sealed class GetCustomersHandler(IReadDbContext db)
    : IQueryHandler<GetCustomersQuery, IReadOnlyList<CustomerDto>>
{
    public async Task<IReadOnlyList<CustomerDto>> Handle(GetCustomersQuery query, CancellationToken ct)
    {
        var customers = await db.Customers.ToListAsync(ct);
        var orders = await db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var byCustomer = orders.ToLookup(o => o.CustomerId);

        // Поиск в памяти: SQLite не умеет сравнивать кириллицу без учёта регистра.
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            customers = customers
                .Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            (c.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return customers
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c =>
            {
                var own = byCustomer[c.Id].ToList();
                return new CustomerDto(
                    c.Id,
                    c.Name,
                    c.Phone,
                    c.Note,
                    own.Count,
                    own.Sum(o => o.Total).Money(),
                    own.Count == 0 ? null : own.Max(o => o.DueDate));
            })
            .ToList();
    }
}

// ---------- Создание ----------

public sealed record CreateCustomerCommand(string Name, string? Phone, string? Note) : ICommand<Guid>;

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Имя клиента");
        RuleFor(x => x.Phone).MaximumLength(50).WithName("Телефон");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
    }
}

public sealed class CreateCustomerHandler(ICustomerRepository customers) : ICommandHandler<CreateCustomerCommand, Guid>
{
    public Task<Guid> Handle(CreateCustomerCommand command, CancellationToken ct)
    {
        var customer = Customer.Create(command.Name, command.Phone, command.Note);
        customers.Add(customer);
        return Task.FromResult(customer.Id);
    }
}

// ---------- Изменение ----------

public sealed record UpdateCustomerCommand(Guid Id, string Name, string? Phone, string? Note) : ICommand<Unit>;

public sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Имя клиента");
        RuleFor(x => x.Phone).MaximumLength(50).WithName("Телефон");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
    }
}

public sealed class UpdateCustomerHandler(ICustomerRepository customers) : ICommandHandler<UpdateCustomerCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCustomerCommand command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Клиент не найден.");
        customer.Update(command.Name, command.Phone, command.Note);
        return Unit.Value;
    }
}

// ---------- Удаление ----------

public sealed record DeleteCustomerCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteCustomerHandler(ICustomerRepository customers, IOrderRepository orders)
    : ICommandHandler<DeleteCustomerCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCustomerCommand command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Клиент не найден.");

        if (await orders.AnyForCustomerAsync(customer.Id, ct))
            throw new DomainException($"У клиента «{customer.Name}» есть заказы, поэтому удалить его нельзя.");

        customers.Remove(customer);
        return Unit.Value;
    }
}
