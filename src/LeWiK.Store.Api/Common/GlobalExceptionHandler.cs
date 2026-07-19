using FluentValidation;
using LeWiK.Store.App.Common.Messaging.Behaviors;
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
            
            case MissingTenantException:
                await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "No tenant resolved").ExecuteAsync(context);
                return true;

            case Microsoft.AspNetCore.Http.BadHttpRequestException:
            case System.Text.Json.JsonException:
                // Malformed JSON, wrong field types, invalid enum values, bad encoding.
                // Detail is generic on purpose: the raw exception can leak model structure.
                await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "request.invalid_body",
                        detail: "The request body could not be read. Check JSON syntax, field types and enum values.")
                    .ExecuteAsync(context);
                return true;

            default:
                logger.LogError(exception, "Unhandled exception");
                await Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpected error occured")
                .ExecuteAsync(context);
                return true;
        }
    }
}