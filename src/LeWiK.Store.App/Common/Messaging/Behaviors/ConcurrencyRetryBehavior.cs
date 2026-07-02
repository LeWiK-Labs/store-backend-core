using LeWiK.Store.App.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeWiK.Store.App.Common.Messaging.Behaviors;

public sealed class ConcurrencyRetryBehavior<TRequest, TResponse>(
    ILogger<ConcurrencyRetryBehavior<TRequest, TResponse>> logger,
    StoreDbContext db)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : ICommandMarker
{
    private const int MaxAttempts = 3;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await next();
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogWarning("Concurrency conflict on {Request}, retry {Attempt}/{Max}",
                    typeof(TRequest).Name, attempt, MaxAttempts);
                // small backoff to de-sync racing requests
                db.ChangeTracker.Clear();
                await Task.Delay(attempt * 25, ct);
            }
        }
    }
}