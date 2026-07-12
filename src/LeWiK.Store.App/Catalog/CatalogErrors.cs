using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Catalog;

public static class CatalogErrors
{
    public static Error DuplicateSku(string sku) => 
        Error.Conflict("catalog.duplicate_sku", $"A product with SKU '{sku}' already exists.");
    public static Error NoVariants() =>
        Error.Validation("catalog.no_variants", "A product must have at least one variant.");
    public static Error DuplicateOptionName() =>
        Error.Validation("catalog.duplicate_option", "Option names must be unique.");
    public static Error EmptyOption(string name) =>
        Error.Validation("catalog.empty_option", $"Option '{name}' must have at least one value.");
    public static Error DuplicateOptionValue(string name) =>
        Error.Validation("catalog.duplicate_option_value", $"Option '{name}' has duplicate values.");
    public static Error IncompleteSelection(string sku) =>
        Error.Validation("catalog.incomplete_selection", $"Variant '{sku}' must select one value per option.");
    public static Error UnknownSelection(string sku, string option, string value) =>
        Error.Validation("catalog.unknown_selection", $"Variant '{sku}' selects unknown value '{value}' for '{option}'.");
    public static Error DuplicateCombination(string sku) =>
        Error.Conflict("catalog.duplicate_combination", $"Variant '{sku}' duplicates an existing option combination.");
    public static Error NoPurchaseLimit(Guid productId) =>
        Error.NotFound("catalog.no_purchase_limit", $"No purchase limit set for product {productId}.");
    public static Error ProductNotFound(Guid productId) =>
        Error.NotFound("catalog.product_not_found", $"Product {productId} was not found.");
}