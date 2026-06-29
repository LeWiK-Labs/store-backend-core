using FluentValidation;
using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog;

// ---- Command (input shape mirrors the JSON body) ----
public sealed record CreateProductWithOptionsCommand(
    string Name,
    string? Description,
    IReadOnlyList<OptionInput> Options,
    IReadOnlyList<VariantInput> Variants) : ICommand<Guid>;

public sealed record OptionInput(string Name, IReadOnlyList<string> Values);

public sealed record VariantInput(
    string Sku,
    decimal Price,
    string Currency,
    IReadOnlyDictionary<string, string> Selections);

// ---- Validator (shape only — business coherence lives in the aggregate) ----
public sealed class CreateProductWithOptionsValidator : AbstractValidator<CreateProductWithOptionsCommand>
{
    public CreateProductWithOptionsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.Options).NotNull();
        RuleForEach(x => x.Options).ChildRules(o =>
        {
            o.RuleFor(v => v.Name).NotEmpty().MaximumLength(100);
            o.RuleFor(v => v.Values).NotEmpty();
            o.RuleForEach(v => v.Values).NotEmpty().MaximumLength(100);
        });

        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
            v.RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
            v.RuleFor(x => x.Currency).NotEmpty().Length(3);
            v.RuleFor(x => x.Selections).NotEmpty();
        });
    }
}

// ---- Handler ----
public sealed class CreateProductWithOptionsHandler(StoreDbContext db, ITenantContext tenant)
    : IRequestHandler<CreateProductWithOptionsCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateProductWithOptionsCommand request, CancellationToken ct)
    {
        var skus = request.Variants.Select(v => v.Sku).ToList();

        // SKUs unique within the request...
        if (skus.Count != skus.Distinct().Count())
            return CatalogErrors.DuplicateSku("(within request)");

        // ...and not colliding with existing variants in this tenant (tenant filter applies).
        var clash = await db.Set<ProductVariant>().AnyAsync(v => skus.Contains(v.Sku), ct);
        if (clash)
            return CatalogErrors.DuplicateSku("(already exists)");

        var options = request.Options.Select(o => new OptionDraft(o.Name, o.Values)).ToList();
        var variants = request.Variants
            .Select(v => new VariantDraft(v.Sku, v.Price, v.Currency, v.Selections))
            .ToList();

        var result = Product.CreateWithOptions(tenant.TenantId, request.Name, request.Description, options, variants);
        if (result.IsFailure)
            return result.Error;

        db.Add(result.Value);
        return result.Value.Id;
    }
}