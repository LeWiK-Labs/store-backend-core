using FluentValidation;
using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Customers;
using LeWiK.Store.App.Customers.Domain;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.App.Orders;

// Customer is optional because a logged-in buyer already has one on file. AuthenticatedCustomerId
// is filled by the endpoint from the session claims and is NOT part of the request body: if a
// caller could name a customer id, checkout would become a way to order on someone else's behalf.
public sealed record CheckoutCommand(
    CustomerInfo? Customer,
    IReadOnlyList<CheckoutItem> Items,
    Guid? AuthenticatedCustomerId = null) : ICommand<CheckoutResponse>;

// The token is the guest's only handle on this order. Once order details stopped being
// readable by bare id — a GUID in a URL was enough to read a stranger's personal data — a
// buyer with no account needs something to hold, and this is it.
//
// ReservationExpiresAt is the deadline the storefront counts down to. It ships with the order
// rather than needing a second call: the buyer has to learn the clock exists at the moment it
// starts, or the feature is a surprise cancellation instead of a countdown.
public sealed record CheckoutResponse(Guid OrderId, string AccessToken, DateTime? ReservationExpiresAt);

public sealed record CustomerInfo(string Email, string Phone, string? Name);
public sealed record CheckoutItem(Guid ProductVariantId, int Quantity);

public sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        // Contact details are only required from a guest. A signed-in buyer already gave them,
        // and demanding them again would make having an account worse than not having one.
        When(x => x.AuthenticatedCustomerId is null, () =>
        {
            RuleFor(x => x.Customer).NotNull();
            // Nested, not flat: FluentValidation runs every RuleFor in a When block, so a flat
            // list would dereference Customer.Email on the null it just rejected — turning a
            // guest checkout with no contact details into a 500 instead of a 400.
            When(x => x.Customer is not null, () =>
            {
                RuleFor(x => x.Customer!.Email).NotEmpty().EmailAddress().MaximumLength(320);
                RuleFor(x => x.Customer!.Phone).NotEmpty().MaximumLength(30);
                RuleFor(x => x.Customer!.Name).MaximumLength(200);
            });
        });
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductVariantId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class CheckoutHandler(
    StoreDbContext db, ITenantContext tenant, PurchaseLimitEnforcer limitEnforcer,
    IOptions<ReservationSettings> reservations)
    : IRequestHandler<CheckoutCommand, Result<CheckoutResponse>>
{
    public async Task<Result<CheckoutResponse>> Handle(CheckoutCommand request, CancellationToken ct)
    {
        // Merge duplicate variants into a single line each.
        var items = request.Items
            .GroupBy(i => i.ProductVariantId)
            .Select(g => (VariantId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        // Resolve the customer: the session's account when there is one, otherwise
        // get-or-create a guest by email within the tenant.
        Customer customer;
        if (request.AuthenticatedCustomerId is { } customerId)
        {
            // Attach the order to their account. Looked up under the normal tenant filter, so a
            // session whose customer belongs to another store finds nothing here even if the
            // policy were ever loosened.
            var account = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (account is null) return CustomerErrors.NotFound();
            customer = account;

            // Contact details are optional now, but a buyer may still correct them at checkout.
            if (request.Customer is not null)
                customer.UpdateContact(request.Customer.Phone, request.Customer.Name ?? customer.Name);
        }
        else
        {
            // Lowercased to match the entity, which normalises on construction. The unique
            // index is (TenantId, Email) and Postgres compares case-sensitively, so without
            // this "Juan@x.cl" would not find the guest created as "juan@x.cl" — and the same
            // person would end up with two customer rows, two histories, and two independent
            // anti-scalping counters.
            var email = request.Customer!.Email.ToLowerInvariant();
            var existing = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Email == email, ct);
            if (existing is null)
            {
                customer = Customer.Guest(tenant.TenantId, email, request.Customer.Phone, request.Customer.Name);
                db.Add(customer);
            }
            else
            {
                existing.UpdateContact(request.Customer.Phone, request.Customer.Name ?? existing.Name);
                customer = existing;
            }
        }

        var drafts = new List<OrderLineDraft>();
        var intents = new List<PurchaseIntent>();
        var depositDue = 0m;
        
        foreach (var item in items)
        {
            var variant = await db.Set<ProductVariant>().FirstOrDefaultAsync(v => v.Id == item.VariantId, ct);
            if (variant is null) return OrderErrors.VariantNotFound(item.VariantId);
            intents.Add(new PurchaseIntent(variant.ProductId, variant.Id, item.Quantity));
        }

        // Enforce anti-scalping limits before touching stock.
        var limitCheck = await limitEnforcer.CheckAsync(customer.Id, intents, ct);
        if (limitCheck.IsFailure) return limitCheck.Error;

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
                drafts.Add(new OrderLineDraft(variant.ProductId, variant.Id, variant.Sku, nameSnapshot,
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
                drafts.Add(new OrderLineDraft(variant.ProductId, variant.Id, variant.Sku, nameSnapshot,
                    variant.Price.Amount, variant.Price.Currency, item.Quantity, IsPreorder: false));
            }
        }

        // Place the order. depositDue == total (all stock) → pay-in-full; < total → deposit flow.
        var currency = drafts[0].Currency;
        var orderResult = Order.Place(tenant.TenantId, customer.Id, currency, drafts, depositDue);
        if (orderResult.IsFailure) return orderResult.Error;

        db.Add(orderResult.Value);

        // Start the soft-lock clock: an unpaid order does not hold stock forever, or one buyer
        // who abandoned a checkout keeps a unit out of the store's window indefinitely.
        if (reservations.Value.Enabled)
            orderResult.Value.SetReservationWindow(
                DateTime.UtcNow.AddMinutes(reservations.Value.TtlMinutes));

        // Issued here rather than by a later admin action: the buyer needs it the instant the
        // order exists, and there is nobody else in the loop to hand it to them. 60 days
        // outlives any reasonable preorder wait.
        var accessToken = OpaqueToken.Generate();
        orderResult.Value.SetPaymentLink(OpaqueToken.Hash(accessToken), DateTime.UtcNow.AddDays(60));

        // UnitOfWorkBehavior commits everything atomically and dispatches domain events.
        return new CheckoutResponse(
            orderResult.Value.Id, accessToken, orderResult.Value.ReservationExpiresAt);
    }
}