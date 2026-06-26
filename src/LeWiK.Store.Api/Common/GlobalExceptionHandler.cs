using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace LeWiK.Store.Api.Common;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ValidationException validation:
                await Results.ValidationProblem(
                        validation.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                        statusCode: StatusCodes.Status400BadRequest)
                    .ExecuteAsync(context);
                return true;
            
            default:
                logger.LogError(exception, "Unhandled exception");
                await Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpeted error occured")
                .ExecuteAsync(context);
                return true;
        }
    }
}