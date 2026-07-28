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
    // Contention on one row is bounded by how many writers target the same aggregate at once:
    // a gateway retrying a webhook, plus a buyer refreshing. 6 attempts absorbs that with room
    // to spare — measured, 3 was not enough for ten simultaneous writers.
    private const int MaxAttempts = 6;

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
                db.ChangeTracker.Clear();
                // Exponential backoff with jitter. A fixed delay makes every loser of a race
                // wake at the same instant and collide again — the retry has to de-sync them,
                // not just postpone them.
                var backoff = (1 << (attempt - 1)) * 20;             // 20, 40, 80, 160, 320 ms
                await Task.Delay(backoff + Random.Shared.Next(backoff), ct);
            }
        }
    }
}