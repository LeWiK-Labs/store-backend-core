using FluentValidation;
using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string Currency) : ICommand<Guid>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public sealed class CreateProductHandler(StoreDbContext db, ITenantContext tentant)
    : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var skuExists = await db.Set<Product>().AnyAsync(p => p.Sku == request.Sku, ct);
        if (skuExists) return CatalogErrors.DuplicateSku(request.Sku);

        var product = new Product(
            tentant.TenantId,
            request.Sku,
            request.Name,
            request.Description,
            new Money(request.Price, request.Currency)
        );

        db.Add(product);

        return product.Id;
    }
}