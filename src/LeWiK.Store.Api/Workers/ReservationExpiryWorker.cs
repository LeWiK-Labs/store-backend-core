using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.Api.Workers;

// Releases stock and preorder capacity held by unpaid orders whose soft-lock window lapsed.
// Lives in Api because it is host infrastructure, like the tenant middleware.
//
// ⚠️ SINGLE INSTANCE. Every instance runs this loop, so two of them sweep the same candidates at
// once: duplicated work and noisy logs, though not corruption — the second cancel answers
// order.cancelled and xmin protects the inventory. Fixing it properly is a Redis lock (Redis is
// already a dependency) around the sweep, ~15 lines, and belongs with whatever else scaling out
// needs. Deliberately not done for a one-instance pilot.
public sealed class ReservationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSettings> settings,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled)
        {
            logger.LogInformation("Reservation expiry is disabled");
            return;
        }

        logger.LogInformation(
            "Reservation expiry sweeping every {Interval}s (TTL {Ttl}min, payment grace {Grace}min)",
            settings.Value.SweepIntervalSeconds, settings.Value.TtlMinutes,
            settings.Value.PaymentGraceMinutes);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Value.SweepIntervalSeconds));
            do
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never let one bad sweep kill the worker: an unhandled exception out of
                    // ExecuteAsync stops the host, which would take the whole API down with it.
                    logger.LogError(ex, "Reservation sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* host shutting down */ }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        List<(Guid OrderId, Guid TenantId)> expired;

        // Candidates across every tenant: the worker runs outside any request, so there is no
        // tenant to filter by yet — hence IgnoreQueryFilters, in the one query that needs it.
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var now = DateTime.UtcNow;

            var candidates = await db.Set<Order>()
                .IgnoreQueryFilters()
                .Where(o => o.FulfillmentStatus == FulfillmentStatus.PendingPayment
                            && o.PaymentStatus == PaymentStatus.Pending   // nothing paid: safe to drop
                            && o.ReservationExpiresAt != null
                            && o.ReservationExpiresAt < now)
                .OrderBy(o => o.ReservationExpiresAt)                     // oldest debt first
                .Take(settings.Value.BatchSize)
                .Select(o => new { o.Id, o.TenantId })
                .ToListAsync(ct);

            expired = [.. candidates.Select(c => (c.Id, c.TenantId))];
        }

        if (expired.Count == 0) return;

        foreach (var (orderId, tenantId) in expired)
        {
            // One scope per order: a fresh DbContext and its own unit of work, with the tenant
            // pinned so CancelOrderCommand runs exactly as it does inside a request — which is the
            // point, because its release logic for stock and preorder capacity is already tested
            // (including F2.4's distinction for preorders already converted to stock).
            //
            // OnlyIfReservationExpired re-checks the precondition inside that transaction: the list
            // above is already stale by the time we get here, and an order that just got paid must
            // survive.
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);

            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new CancelOrderCommand(orderId, OnlyIfReservationExpired: true), ct);

            if (result.IsSuccess)
                logger.LogInformation("Expired reservation for order {OrderId} (tenant {TenantId})",
                    orderId, tenantId);
            else if (result.Error.Code == "order.reservation_not_expired")
                // Not a problem: the order was paid or cancelled while we were working through the
                // batch, and the guard did its job.
                logger.LogDebug("Order {OrderId} no longer expiring", orderId);
            else
                logger.LogWarning("Could not expire order {OrderId}: {Code}", orderId, result.Error.Code);
        }
    }
}
