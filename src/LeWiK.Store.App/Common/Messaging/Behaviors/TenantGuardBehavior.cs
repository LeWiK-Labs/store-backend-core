using LeWiK.Store.App.Common.Tenancy;
using MediatR;

namespace LeWiK.Store.App.Common.Messaging.Behaviors;

public sealed class TenantGuardBehavior<TRequest, TResponse>(ITenantContext tenant) : IPipelineBehavior<TRequest,TResponse> where TRequest : ICommandMarker
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!tenant.HasTenant) throw new MissingTenantException();
        
        return next();
    }
}

public sealed class MissingTenantException() : Exception("No tenant war resolved for this request.");