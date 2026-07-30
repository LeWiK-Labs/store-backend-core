using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// ITenantAgnostic: an operator does not belong to a store, so there is no tenant to guard.
// Without the marker the tenant guard would reject every platform login outright.
public sealed record LoginPlatformOperatorCommand(string Email, string Password, string? UserAgent)
    : ICommand<SessionIssued>, ITenantAgnostic;

public sealed class LoginPlatformOperatorValidator : AbstractValidator<LoginPlatformOperatorCommand>
{
    public LoginPlatformOperatorValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginPlatformOperatorHandler(StoreDbContext db, PasswordHasher hasher)
    : IRequestHandler<LoginPlatformOperatorCommand, Result<SessionIssued>>
{
    // Shorter than a staff shift: these credentials can reach every store on the platform.
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public async Task<Result<SessionIssued>> Handle(LoginPlatformOperatorCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var op = await db.Set<PlatformOperator>().FirstOrDefaultAsync(o => o.Email == email, ct);

        if (op is null)
        {
            hasher.SpendVerificationTime();
            return PlatformErrors.InvalidCredentials();
        }
        if (!hasher.Verify(op.PasswordHash, request.Password))
            return PlatformErrors.InvalidCredentials();
        if (!op.IsActive) return PlatformErrors.AccountDisabled();

        var token = OpaqueToken.Generate();
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        db.Add(new PlatformSession(op.Id, OpaqueToken.Hash(token), expiresAt, request.UserAgent));
        op.RecordLogin();

        return new SessionIssued(token, expiresAt, op.Name, "PlatformOperator");
    }
}
