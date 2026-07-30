using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// Staff login DOES need a tenant: you log into a particular store's panel, and the same email
// may exist at two stores. The tenant guard applies here on purpose.
public sealed record LoginStaffCommand(string Email, string Password, string? UserAgent)
    : ICommand<SessionIssued>;

// The raw token is returned exactly once, for the API layer to put in a cookie. It is never
// stored, so it can never be read back — same rule as the payment link.
public sealed record SessionIssued(string Token, DateTime ExpiresAt, string DisplayName, string Role);

public sealed class LoginStaffValidator : AbstractValidator<LoginStaffCommand>
{
    public LoginStaffValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginStaffHandler(StoreDbContext db, ITenantContext tenant, PasswordHasher hasher)
    : IRequestHandler<LoginStaffCommand, Result<SessionIssued>>
{
    // A shift-length session: long enough to cover a POS day without re-login, short enough
    // that a forgotten browser is not a standing key.
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public async Task<Result<SessionIssued>> Handle(LoginStaffCommand request, CancellationToken ct)
    {
        var store = await db.Set<Domain.Store>().FirstOrDefaultAsync(s => s.Id == tenant.TenantId, ct);
        if (store is null) return PlatformErrors.StoreNotFound(tenant.TenantId);
        if (!store.IsActive) return PlatformErrors.StoreSuspended();

        var email = request.Email.ToLowerInvariant();
        var user = await db.Set<StaffUser>().FirstOrDefaultAsync(u => u.Email == email, ct);

        // One indistinguishable answer whether the account is missing or the password is wrong,
        // and the same time spent either way — otherwise the clock leaks what the message hides.
        if (user is null)
        {
            hasher.SpendVerificationTime();
            return PlatformErrors.InvalidCredentials();
        }
        if (!hasher.Verify(user.PasswordHash, request.Password))
            return PlatformErrors.InvalidCredentials();

        // Checked after the password so a disabled account is not revealed to a stranger.
        if (!user.IsActive) return PlatformErrors.AccountDisabled();

        var token = OpaqueToken.Generate();
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        db.Add(new StaffSession(store.Id, user.Id, OpaqueToken.Hash(token), expiresAt, request.UserAgent));
        user.RecordLogin();

        return new SessionIssued(token, expiresAt, user.Name, user.Role.ToString());
    }
}
