using LeWiK.Store.App.Inventory.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LeWiK.Store.App.Inventory;

public sealed class StockChangedHandler(ILogger<StockChangedHandler> logger) : INotificationHandler<StockChanged>
{
    public Task Handle(StockChanged notification, CancellationToken ct)
    {
        logger.LogInformation(
            "StockChanged: variant {Variant} -> {Available} available, {Reserved} reserved",
            notification.ProductVariantId, notification.Available, notification.Reserved);
        return Task.CompletedTask;
    }
}