using KOSHI.Application.Common;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KOSHI.Api.Infrastructure;

/// <summary>Maps expected errors to RFC 7807 responses; unexpected errors return a generic 500 without internals.</summary>
public sealed class KoshiExceptionHandler(ILogger<KoshiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        if (exception is AppException app)
        {
            problem = app.Errors is { Count: > 0 }
                ? new ValidationProblemDetails(app.Errors) { Status = app.StatusCode, Title = app.Message }
                : new ProblemDetails { Status = app.StatusCode, Title = app.Message };
            problem.Extensions["code"] = app.Code;
            if (app.StatusCode >= 500) logger.LogError(exception, "Application error {Code}", app.Code);
            else logger.LogInformation("Request rejected: {Code} {Status}", app.Code, app.StatusCode);
        }
        else if (exception is BadHttpRequestException bad)
        {
            problem = new ProblemDetails { Status = bad.StatusCode, Title = "Bad request." };
            problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        }
        else if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }
        else
        {
            logger.LogError(exception, "Unhandled exception");
            problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
            problem.Extensions["code"] = ErrorCodes.InternalError;
        }

        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
