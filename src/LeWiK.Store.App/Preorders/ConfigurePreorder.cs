using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Preorders;
public sealed record ConfigurePreorderCommand(
    Guid ProductVariantId,
    int Capacity,
    DateTime ReleaseDate,
    DepositType DepositType,
    decimal DepositValue) : ICommand<PreorderResponse>;

public sealed record PreorderResponse(
    Guid ProductVariantId, int Capacity, int SoldCount, int AvailableCapacity,
    DateTime ReleaseDate, string DepositType, decimal DepositValue, string Status);

public sealed class ConfigurePreorderValidator : AbstractValidator<ConfigurePreorderCommand>
{
    public ConfigurePreorderValidator()
    {
        RuleFor(x => x.ProductVariantId).NotEmpty();
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.ReleaseDate).NotEmpty();

        // Deposit shape depends on its type.
        When(x => x.DepositType == DepositType.Percentage, () =>
            RuleFor(x => x.DepositValue).GreaterThan(0).LessThanOrEqualTo(100));

        When(x => x.DepositType == DepositType.FixedPerUnit, () =>
            RuleFor(x => x.DepositValue).GreaterThan(0));
    }
}

public sealed class ConfigurePreorderHandler(StoreDbContext db, ITenantContext tenant)
    : IRequestHandler<ConfigurePreorderCommand, Result<PreorderResponse>>
{
    public async Task<Result<PreorderResponse>> Handle(ConfigurePreorderCommand request, CancellationToken ct)
    {
        // Get-or-create the preorder for this variant (tenant filter applies).
        var preorder = await db.Set<Preorder>()
            .FirstOrDefaultAsync(p => p.ProductVariantId == request.ProductVariantId, ct);

        if (preorder is null)
        {
            preorder = new Preorder(tenant.TenantId, request.ProductVariantId,
                request.Capacity, request.ReleaseDate, request.DepositType, request.DepositValue);
            db.Add(preorder);
        }
        else
        {
            var reconfig = preorder.Reconfigure(
                request.Capacity, request.ReleaseDate, request.DepositType, request.DepositValue);
            if (reconfig.IsFailure)
                return reconfig.Error;
        }

        return Map(preorder);
    }

    internal static PreorderResponse Map(Preorder p) => new(
        p.ProductVariantId, p.Capacity, p.SoldCount, p.AvailableCapacity,
        p.ReleaseDate, p.DepositType.ToString(), p.DepositValue, p.Status.ToString());
}