using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Api;

/// <summary>Превращает исключения в ответы ProblemDetails с понятным русским текстом.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            ValidationFailedException validation => (
                StatusCodes.Status400BadRequest,
                "Проверьте введённые данные",
                string.Join(" ", validation.Errors.SelectMany(e => e.Value))),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Действие невозможно", exception.Message),
            NotFoundException => (StatusCodes.Status404NotFound, "Не найдено", exception.Message),
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                "Некорректный запрос",
                "Не удалось прочитать данные запроса. Проверьте заполнение полей."),
            DbUpdateException => (
                StatusCodes.Status409Conflict,
                "Не удалось сохранить",
                "Запись используется в других данных или уже существует."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Внутренняя ошибка",
                "Что-то пошло не так. Попробуйте ещё раз."),
        };

        if (status >= 500 || exception is DbUpdateException)
            logger.LogError(exception, "Необработанная ошибка при запросе {Path}", httpContext.Request.Path);

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (exception is ValidationFailedException failed)
            problem.Extensions["errors"] = failed.Errors;

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
