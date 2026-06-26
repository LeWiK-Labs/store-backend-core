using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;

namespace LeWiK.Store.App.Common.Messaging.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse>(StoreDbContext db) : IPipelineBehavior<TRequest, TResponse> where TRequest : ICommandMarker
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();

        if (response is Result { IsFailure: true })
            return response;

        await db.SaveChangesAsync(ct);
        return response;
    }
}