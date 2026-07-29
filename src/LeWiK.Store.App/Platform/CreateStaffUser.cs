using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// NOT tenant-agnostic: a staff user belongs to the store adding them, which comes from the
// ambient tenant like every other store-level command.
public sealed record CreateStaffUserCommand(string Email, string Name, string Password, StaffRole Role)
    : ICommand<StaffUserResponse>;

// No hash in the response, ever — not even to the admin who just set it.
public sealed record StaffUserResponse(Guid Id, string Email, string Name, string Role, bool IsActive);

public sealed class CreateStaffUserValidator : AbstractValidator<CreateStaffUserCommand>
{
    public CreateStaffUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class CreateStaffUserHandler(StoreDbContext db, ITenantContext tenant, PasswordHasher hasher)
    : IRequestHandler<CreateStaffUserCommand, Result<StaffUserResponse>>
{
    public async Task<Result<StaffUserResponse>> Handle(CreateStaffUserCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        // The tenant filter applies here, so this asks "is the email taken IN THIS STORE" —
        // which is the question the unique index answers too.
        if (await db.Set<StaffUser>().AnyAsync(u => u.Email == email, ct))
            return PlatformErrors.StaffEmailTaken(email);

        var user = new StaffUser(tenant.TenantId, email, request.Name, hasher.Hash(request.Password), request.Role);
        db.Add(user);

        return new StaffUserResponse(user.Id, user.Email, user.Name, user.Role.ToString(), user.IsActive);
    }
}
