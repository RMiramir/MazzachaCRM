namespace MazzachaCRM.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Проверьте введённые данные.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
