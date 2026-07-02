using FluentValidation;
using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Customers.Domain;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record CheckoutCommand(
    CustomerInfo Customer,
    IReadOnlyList<CheckoutItem> Items) : ICommand<Guid>;

public sealed record CustomerInfo(string Email, string Phone, string? Name);
public sealed record CheckoutItem(Guid ProductVariantId, int Quantity);

public sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.Customer.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Customer.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Customer.Name).MaximumLength(200);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductVariantId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class CheckoutHandler(StoreDbContext db, ITenantContext tenant)
    : IRequestHandler<CheckoutCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CheckoutCommand request, CancellationToken ct)
    {
        // Merge duplicate variants into a single line each.
        var items = request.Items
            .GroupBy(i => i.ProductVariantId)
            .Select(g => (VariantId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        // Resolve the customer: get-or-create a guest by email within the tenant.
        var customer = await db.Set<Customer>()
            .FirstOrDefaultAsync(c => c.Email == request.Customer.Email, ct);
        if (customer is null)
        {
            customer = Customer.Guest(tenant.TenantId, request.Customer.Email, request.Customer.Phone, request.Customer.Name);
            db.Add(customer);
        }
        else
        {
            customer.UpdateContact(request.Customer.Phone, request.Customer.Name);
        }

        var drafts = new List<OrderLineDraft>();
        var depositDue = 0m;

        foreach (var item in items)
        {
            // Read the variant for its price/name snapshot (crosses into Catalog).
            var variant = await db.Set<ProductVariant>()
                .FirstOrDefaultAsync(v => v.Id == item.VariantId, ct);
            if (variant is null)
                return OrderErrors.VariantNotFound(item.VariantId);

            var productName = await db.Set<Product>()
                .Where(p => p.Id == variant.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            var nameSnapshot = variant.Label == "Default" ? productName! : $"{productName} - {variant.Label}";

            // A variant with an active preorder is sold as a drop; otherwise from stock.
            var preorder = await db.Set<Preorder>()
                .FirstOrDefaultAsync(p => p.ProductVariantId == item.VariantId, ct);

            if (preorder is { Status: PreorderStatus.Active })
            {
                // SOFT-LOCK (preorder capacity). Phase 4: this reservation gets the order's TTL.
                var reserve = preorder.ReserveCapacity(item.Quantity);
                if (reserve.IsFailure) return reserve.Error;

                depositDue += preorder.CalculateDeposit(variant.Price, item.Quantity).Amount;
                drafts.Add(new OrderLineDraft(variant.Id, variant.Sku, nameSnapshot,
                    variant.Price.Amount, variant.Price.Currency, item.Quantity, IsPreorder: true));
            }
            else
            {
                var inventory = await db.Set<InventoryItem>()
                    .FirstOrDefaultAsync(i => i.ProductVariantId == item.VariantId, ct);
                if (inventory is null)
                    return OrderErrors.VariantHasNoStock(item.VariantId);

                // SOFT-LOCK (physical stock). Phase 4: this reservation gets the order's TTL.
                var reserve = inventory.Reserve(item.Quantity);
                if (reserve.IsFailure) return reserve.Error;

                depositDue += variant.Price.Multiply(item.Quantity).Amount;
                drafts.Add(new OrderLineDraft(variant.Id, variant.Sku, nameSnapshot,
                    variant.Price.Amount, variant.Price.Currency, item.Quantity, IsPreorder: false));
            }
        }

        // Place the order. depositDue == total (all stock) → pay-in-full; < total → deposit flow.
        var currency = drafts[0].Currency;
        var orderResult = Order.Place(tenant.TenantId, customer.Id, currency, drafts, depositDue);
        if (orderResult.IsFailure) return orderResult.Error;

        db.Add(orderResult.Value);
        // UnitOfWorkBehavior commits everything atomically and dispatches domain events.
        return orderResult.Value.Id;
    }
}