using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;

namespace LeWiK.Store.App.Common.Messaging.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse>(StoreDbContext db, IPublisher publisher) : IPipelineBehavior<TRequest, TResponse> where TRequest : ICommandMarker
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();

        if (response is Result { IsFailure: true })
            return response;

        await db.SaveChangesAsync(ct);
        await DispatchDomainEventsAsync(ct);   // ← esta línea faltaba
        return response;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count != 0)
            .Select(e => e.Entity)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());
        
        foreach (var domainEvent in events)
            await publisher.Publish(domainEvent, ct);
    }
}