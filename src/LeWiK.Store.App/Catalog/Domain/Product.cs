using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Catalog.Domain;

public enum ProductStatus { Active, Inactive}

public sealed record OptionDraft(string Name, IReadOnlyList<string> Values);
public sealed record VariantDraft(string Sku, decimal Price, string Currency,
    IReadOnlyDictionary<string, string> Selections);

public sealed class Product : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private readonly List<ProductVariant> _variants = [];
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    
    private readonly List<ProductOption>  _options = [];
    public IReadOnlyCollection<ProductOption> Options => _options.AsReadOnly();
    
    private Product() {}

    // Bare constructor: product fields only, no variants. Used by both creation paths.
    private Product(Guid tenantId, string name, string? description)
    {
        Id = Guid.CreateVersion7(); // Guid.NewGuid() on .NET 8
        TenantId = tenantId;
        Name = name;
        Description = description;
        Status = ProductStatus.Active;
    }

    // Simple product: one default variant (sku + price on the variant). Phase 1 path.
    public static Product CreateSimple(Guid tenantId, string name, string? description, string defaultSku, Money defaultPrice)
    {
        var product = new Product(tenantId, name, description);
        product._variants.Add(ProductVariant.CreateDefault(product, defaultSku, defaultPrice));
        return product;
    }

    public ProductVariant AddVariant(string sku, string label, Money price)
    {
        var variant = ProductVariant.Create(this, sku, label, price);
        _variants.Add(variant);
        return variant;
    }

    public static Result<Product> CreateWithOptions(Guid tenantId, string name, string? description,
        IReadOnlyList<OptionDraft> options, IReadOnlyList<VariantDraft> variants)
    {
        if (variants.Count == 0) return CatalogErrors.NoVariants();
        
        var optionNames = options.Select(o => o.Name).ToList();
        if (optionNames.Count != optionNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return CatalogErrors.DuplicateOptionName();

        foreach (var opt in options)
        {
            if (opt.Values.Count == 0) return CatalogErrors.EmptyOption(opt.Name);
            if (opt.Values.Count != opt.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                return CatalogErrors.DuplicateOptionValue(opt.Name);
        }

        var product = new Product(tenantId, name, description);
        
        var valueIndex = new Dictionary<(string Option, string Value), ProductOptionValue>();
        var pos = 0;
        foreach (var opt in options)
        {
            var option = new ProductOption(product.Id, opt.Name, pos++, opt.Values);
            product._options.Add(option);
            foreach (var v in option.Values)
                valueIndex[(opt.Name.ToLowerInvariant(), v.Value.ToLowerInvariant())] = v;
        }
        
        var seenCombos = new HashSet<string>();
        foreach (var draft in variants)
        {
            if (draft.Selections.Count != options.Count)
                return CatalogErrors.IncompleteSelection(draft.Sku);

            var valueIds = new List<Guid>();
            foreach (var opt in options)
            {
                if(!draft.Selections.TryGetValue(opt.Name, out var chosen))
                    return CatalogErrors.IncompleteSelection(draft.Sku);
                if(!valueIndex.TryGetValue((opt.Name.ToLowerInvariant(), chosen.ToLowerInvariant()), out var optionValue))
                    return CatalogErrors.UnknownSelection(draft.Sku, opt.Name, chosen);
                valueIds.Add(optionValue.Id);
            }

            var comboKey = string.Join("|", valueIds.OrderBy(id => id));
            if (!seenCombos.Add(comboKey))
                return CatalogErrors.DuplicateCombination(draft.Sku);
            
            var variant = product.AddVariant(draft.Sku, BuildLabel(options, draft), new Money(draft.Price, draft.Currency));
            foreach (var id in valueIds)
                variant.LinkOptionValue(id);
        }

        return product;
    }

    private static string BuildLabel(IReadOnlyList<OptionDraft> options, VariantDraft draft) =>
        string.Join(" / ", options.Select(o => draft.Selections[o.Name]));
}