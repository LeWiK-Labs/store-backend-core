using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Catalog;

public static class CatalogErrors
{
    public static Error DuplicateSku(string sku) => Error.Conflict("catalog.duplicate_sku", $"A product with SKU '{sku}' already exists.");
}