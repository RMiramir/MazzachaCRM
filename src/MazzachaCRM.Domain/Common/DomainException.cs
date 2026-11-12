namespace MazzachaCRM.Domain.Common;

/// <summary>Нарушение бизнес-правила. Текст показывается пользователю как есть.</summary>
public sealed class DomainException(string message) : Exception(message);
