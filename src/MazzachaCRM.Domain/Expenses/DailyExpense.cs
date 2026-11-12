using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Expenses;

/// <summary>Общий расход дня, не привязанный к изделию: такси, аренда места, газ.</summary>
public sealed class DailyExpense : AggregateRoot
{
    private DailyExpense()
    {
    }

    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    public static DailyExpense Create(DateOnly date, string name, decimal amount)
    {
        var expense = new DailyExpense { Date = date };
        expense.Update(name, amount);
        return expense;
    }

    public void Update(string name, decimal amount)
    {
        Name = Guard.NotEmpty(name, "Название расхода");
        Amount = Guard.Positive(amount, "Сумма");
    }
}

public interface IDailyExpenseRepository
{
    Task<DailyExpense?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(DailyExpense expense);
    void Remove(DailyExpense expense);
}
