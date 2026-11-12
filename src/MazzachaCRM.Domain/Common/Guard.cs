namespace MazzachaCRM.Domain.Common;

public static class Guard
{
    public static string NotEmpty(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"Поле «{field}» не может быть пустым.");
        return value.Trim();
    }

    public static decimal Positive(decimal value, string field)
    {
        if (value <= 0)
            throw new DomainException($"Значение «{field}» должно быть больше нуля.");
        return value;
    }

    public static decimal NotNegative(decimal value, string field)
    {
        if (value < 0)
            throw new DomainException($"Значение «{field}» не может быть отрицательным.");
        return value;
    }

    public static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
