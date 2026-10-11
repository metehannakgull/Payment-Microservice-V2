using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Abstractions.Idempotency;

namespace Payment.Api.Exceptions
{
    public sealed class GlobalExceptionHandler
        : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var statusCode = exception switch
            {
                IdempotencyConflictException =>
                    StatusCodes.Status409Conflict,

                IdempotencyInProgressException =>
                    StatusCodes.Status409Conflict,

                ArgumentException =>
                    StatusCodes.Status400BadRequest,

                _ =>
                    StatusCodes.Status500InternalServerError
            };

            if (statusCode >= 500)
            {
                _logger.LogError(
                    exception,
                    "Unhandled exception occurred.");
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "Request failed with status code {StatusCode}.",
                    statusCode);
            }

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),

                Detail = statusCode ==
                         StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : exception.Message,

                Instance = httpContext.Request.Path
            };

            problemDetails.Extensions["traceId"] =
                httpContext.TraceIdentifier;

            httpContext.Response.StatusCode = statusCode;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                cancellationToken);

            return true;
        }

        private static string GetTitle(int statusCode)
        {
            return statusCode switch
            {
                StatusCodes.Status400BadRequest =>
                    "Bad Request",

                StatusCodes.Status409Conflict =>
                    "Conflict",

                StatusCodes.Status500InternalServerError =>
                    "Internal Server Error",

                _ =>
                    "Error"
            };
        }
    }
}
