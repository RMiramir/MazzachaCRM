using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Expenses;

namespace MazzachaCRM.Application.Expenses;

public sealed record ExpenseDto(Guid Id, DateOnly Date, string Name, decimal Amount);

public sealed record AddExpenseCommand(DateOnly Date, string Name, decimal Amount) : ICommand<Guid>;

public sealed class AddExpenseValidator : AbstractValidator<AddExpenseCommand>
{
    public AddExpenseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название расхода");
        RuleFor(x => x.Amount).GreaterThan(0).WithName("Сумма");
    }
}

public sealed class AddExpenseHandler(IDailyExpenseRepository expenses) : ICommandHandler<AddExpenseCommand, Guid>
{
    public Task<Guid> Handle(AddExpenseCommand command, CancellationToken ct)
    {
        var expense = DailyExpense.Create(command.Date, command.Name, command.Amount);
        expenses.Add(expense);
        return Task.FromResult(expense.Id);
    }
}

public sealed record UpdateExpenseCommand(Guid Id, string Name, decimal Amount) : ICommand<Unit>;

public sealed class UpdateExpenseValidator : AbstractValidator<UpdateExpenseCommand>
{
    public UpdateExpenseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название расхода");
        RuleFor(x => x.Amount).GreaterThan(0).WithName("Сумма");
    }
}

public sealed class UpdateExpenseHandler(IDailyExpenseRepository expenses) : ICommandHandler<UpdateExpenseCommand, Unit>
{
    public async Task<Unit> Handle(UpdateExpenseCommand command, CancellationToken ct)
    {
        var expense = await expenses.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Расход не найден.");
        expense.Update(command.Name, command.Amount);
        return Unit.Value;
    }
}

public sealed record DeleteExpenseCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteExpenseHandler(IDailyExpenseRepository expenses) : ICommandHandler<DeleteExpenseCommand, Unit>
{
    public async Task<Unit> Handle(DeleteExpenseCommand command, CancellationToken ct)
    {
        var expense = await expenses.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Расход не найден.");
        expenses.Remove(expense);
        return Unit.Value;
    }
}
