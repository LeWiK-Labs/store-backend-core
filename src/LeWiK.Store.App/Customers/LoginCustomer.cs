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

// Needs a tenant, like staff login: you log into a particular store, and the same email may
// be a customer of two of them with different passwords.
public sealed record LoginCustomerCommand(string Email, string Password, string? UserAgent)
    : ICommand<CustomerSessionIssued>;

public sealed class LoginCustomerValidator : AbstractValidator<LoginCustomerCommand>
{
    public LoginCustomerValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginCustomerHandler(StoreDbContext db, ITenantContext tenant, PasswordHasher hasher)
    : IRequestHandler<LoginCustomerCommand, Result<CustomerSessionIssued>>
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<Result<CustomerSessionIssued>> Handle(LoginCustomerCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var customer = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Email == email, ct);

        // Guests have no password, so they fail exactly like an unknown address: one generic
        // error either way, revealing neither which emails exist nor which ones have accounts.
        //
        // Spending the PBKDF2 time when there is no hash to verify is the other half of that
        // promise. Without it "never bought here" answers in a millisecond while "wrong
        // password" takes a hundred, and the clock leaks what the message withholds — which on
        // a storefront means a list of a store's customers.
        if (customer?.PasswordHash is null)
        {
            hasher.SpendVerificationTime();
            return CustomerErrors.InvalidCredentials();
        }

        if (!hasher.Verify(customer.PasswordHash, request.Password))
            return CustomerErrors.InvalidCredentials();

        var token = OpaqueToken.Generate();
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        db.Add(new CustomerSession(tenant.TenantId, customer.Id, OpaqueToken.Hash(token), expiresAt, request.UserAgent));

        return new CustomerSessionIssued(token, expiresAt, customer.Name, customer.Email);
    }
}
