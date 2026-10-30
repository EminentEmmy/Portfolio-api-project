using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PortfolioApi.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case RequestValidationException v:                       // NEW
                problem = new ValidationProblemDetails(v.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                break;

            case NotFoundException nf:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not found",
                    Detail = nf.Message
                };
                break;

            case ConflictException c:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = c.Title,
                    Detail = c.Message
                };
                break;

            case DbUpdateConcurrencyException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Concurrent change",
                    Detail = "The record was modified by another request. Please retry."
                };
                break;

            default:
                // Appended {TraceId} field so log parsing engines extract it natively
                _logger.LogError(exception, "Unhandled exception on {Method} {Path} ({TraceId})",
                    httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred."
                };
                break;

        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        if (problem is ValidationProblemDetails validationProblem)
        {
            await httpContext.Response.WriteAsJsonAsync(validationProblem, cancellationToken);
        }
        else
        {
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }

        return true;
    }
}