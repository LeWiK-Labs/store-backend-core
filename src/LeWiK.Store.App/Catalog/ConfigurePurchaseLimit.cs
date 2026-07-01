using FluentValidation;
using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog;

public sealed record ConfigurePurchaseLimitCommand(
    PurchaseLimitScope Scope,
    Guid TargetId,
    int? MaxPerOrder,
    int? MaxPerCustomer,
    int? WindowDays) : ICommand<PurchaseLimitResponse>;
    
public sealed record PurchaseLimitResponse(
    string Scope, Guid TargetId, int? MaxPerOrder, int? MaxPerCustomer, int? WindowDays);

public sealed class ConfigurePurchaseLimitValidator : AbstractValidator<ConfigurePurchaseLimitCommand>
{
    public ConfigurePurchaseLimitValidator()
    {
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.MaxPerOrder).GreaterThan(0).When(x => x.MaxPerOrder.HasValue);
        RuleFor(x => x.MaxPerCustomer).GreaterThan(0).When(x => x.MaxPerCustomer.HasValue);
        RuleFor(x => x.WindowDays).GreaterThan(0).When(x => x.WindowDays.HasValue);

        RuleFor(x => x)
            .Must(x => x.MaxPerOrder.HasValue || x.MaxPerCustomer.HasValue)
            .WithMessage("At least one of MaxPerOrder or MaxPerCustomer must be set.");
    }
}

public sealed class ConfigurePurchaseLimitHandler(StoreDbContext db, ITenantContext tenant)
    : IRequestHandler<ConfigurePurchaseLimitCommand, Result<PurchaseLimitResponse>>
{
    public async Task<Result<PurchaseLimitResponse>> Handle(ConfigurePurchaseLimitCommand request, CancellationToken ct)
    {
        // Get-or-create the policy for this scope+target (tenant filter applies).
        var limit = await db.Set<PurchaseLimit>()
            .FirstOrDefaultAsync(l => l.Scope == request.Scope && l.TargetId == request.TargetId, ct);

        if (limit is null)
        {
            limit = new PurchaseLimit(tenant.TenantId, request.Scope, request.TargetId,
                request.MaxPerOrder, request.MaxPerCustomer, request.WindowDays);
            db.Add(limit);
        }
        else
        {
            limit.Set(request.MaxPerOrder, request.MaxPerCustomer, request.WindowDays);
        }

        return new PurchaseLimitResponse(
            limit.Scope.ToString(), limit.TargetId, limit.MaxPerOrder, limit.MaxPerCustomer, limit.WindowDays);
    }
}