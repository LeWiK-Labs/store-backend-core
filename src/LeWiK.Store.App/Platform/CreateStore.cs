using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// ITenantAgnostic: this is the command that CREATES a tenant, so there is nothing for the
// tenant guard to check. It is the one command in the system where "no tenant" is correct.
public sealed record CreateStoreCommand(
    string Name, string Slug, string? CustomDomain,
    string OwnerEmail, string OwnerName, string OwnerPassword) : ICommand<StoreResponse>, ITenantAgnostic;

public sealed record StoreResponse(
    Guid Id, string Name, string Slug, string? CustomDomain, string Status);

public sealed class CreateStoreValidator : AbstractValidator<CreateStoreCommand>
{
    public CreateStoreValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(63)
            .Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")
            .WithMessage("Slug must be a valid DNS label: lowercase letters, digits and hyphens.");
        RuleFor(x => x.CustomDomain).MaximumLength(253);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

public sealed class CreateStoreHandler(StoreDbContext db, PasswordHasher hasher)
    : IRequestHandler<CreateStoreCommand, Result<StoreResponse>>
{
    public async Task<Result<StoreResponse>> Handle(CreateStoreCommand request, CancellationToken ct)
    {
        var slug = request.Slug.ToLowerInvariant();
        if (await db.Set<Domain.Store>().AnyAsync(s => s.Slug == slug, ct))
            return PlatformErrors.SlugTaken(slug);

        var domain = request.CustomDomain?.ToLowerInvariant();
        if (domain is not null && await db.Set<Domain.Store>().AnyAsync(s => s.CustomDomain == domain, ct))
            return PlatformErrors.DomainTaken(domain);

        var store = new Domain.Store(request.Name, slug, domain);
        db.Add(store);

        // A store is born with its owner: a store nobody can administer is not a useful store,
        // and creating them separately leaves a window where one exists without the other.
        db.Add(new StaffUser(store.Id, request.OwnerEmail, request.OwnerName,
            hasher.Hash(request.OwnerPassword), StaffRole.Owner));

        return new StoreResponse(store.Id, store.Name, store.Slug, store.CustomDomain, store.Status.ToString());
    }
}
