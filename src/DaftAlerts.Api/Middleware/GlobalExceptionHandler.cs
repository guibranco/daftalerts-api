using System;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DaftAlerts.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, System.Threading.CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException ve => ToValidationProblem(ve),
            ArgumentException ae => new ProblemDetails
            {
                Type = "about:blank",
                Title = "Bad request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ae.Message
            },
            KeyNotFoundException => new ProblemDetails
            {
                Type = "about:blank",
                Title = "Not found",
                Status = StatusCodes.Status404NotFound
            },
            _ => new ProblemDetails
            {
                Type = "about:blank",
                Title = "Unexpected error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred."
            }
        };

        if (problem.Status >= 500)
            _logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        else
            _logger.LogWarning(exception, "Handled exception on {Path}", httpContext.Request.Path);

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status ?? 500;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails ToValidationProblem(ValidationException ve)
    {
        var vp = new ValidationProblemDetails
        {
            Type = "about:blank",
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        };
        foreach (var f in ve.Errors)
        {
            var key = f.PropertyName ?? string.Empty;
            if (!vp.Errors.TryGetValue(key, out var arr))
            {
                vp.Errors[key] = new[] { f.ErrorMessage };
            }
            else
            {
                var list = new string[arr.Length + 1];
                arr.CopyTo(list, 0);
                list[^1] = f.ErrorMessage;
                vp.Errors[key] = list;
            }
        }
        return vp;
    }
}
