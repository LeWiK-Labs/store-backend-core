using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Customers.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Customers;

public sealed record RegisterCustomerCommand(
    string Email, string Password, string Phone, string? Name, string? UserAgent) : ICommand<CustomerSessionIssued>;

// The raw token is returned exactly once, for the API layer to put in a cookie. Never stored,
// so it can never be read back — same rule as the staff session and the payment link.
public sealed record CustomerSessionIssued(string Token, DateTime ExpiresAt, string? Name, string Email);

public sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).MaximumLength(200);
    }
}

public sealed class RegisterCustomerHandler(StoreDbContext db, ITenantContext tenant, PasswordHasher hasher)
    : IRequestHandler<RegisterCustomerCommand, Result<CustomerSessionIssued>>
{
    // A storefront login is not a shift: buyers expect to stay signed in between visits.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<Result<CustomerSessionIssued>> Handle(RegisterCustomerCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var existing = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Email == email, ct);

        Customer customer;
        if (existing is null)
        {
            customer = Customer.Registered(tenant.TenantId, email, request.Phone, request.Name,
                hasher.Hash(request.Password));
            db.Add(customer);
        }
        else if (existing.IsRegistered)
        {
            return CustomerErrors.AlreadyRegistered(email);
        }
        else
        {
            // Guest who bought before: promote the same record so their order history becomes
            // visible in their new account. This is the payoff of modelling guests and
            // registered buyers as one Customer.
            //
            // ⚠️ KNOWN GAP, accepted deliberately: nothing here proves the registrant owns the
            // address. Anyone who knows an email that has bought here as a guest can register
            // it and inherit that history — order totals, line items, and the contact name and
            // phone, which this call then overwrites. The fix is email verification at signup,
            // which is deferred with the rest of email delivery; there is no cheaper one, since
            // the guest by definition has no credential to prove with. Until then this is a
            // real data-exposure path and not merely a theoretical one.
            existing.PromoteToRegistered(hasher.Hash(request.Password));
            // Keep the name we already knew when the registrant does not supply one: losing it
            // is a plain regression for the store's own records.
            existing.UpdateContact(request.Phone, request.Name ?? existing.Name);
            customer = existing;
        }

        var token = OpaqueToken.Generate();
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        db.Add(new CustomerSession(tenant.TenantId, customer.Id, OpaqueToken.Hash(token), expiresAt, request.UserAgent));

        return new CustomerSessionIssued(token, expiresAt, customer.Name, customer.Email);
    }
}
