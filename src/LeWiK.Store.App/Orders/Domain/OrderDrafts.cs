namespace LeWiK.Store.App.Orders.Domain;

public sealed record OrderLineDraft(
    Guid ProductVariantId, string Sku, string NameSnapshot,
    decimal UnitPrice, string Currency, int Quantity, bool IsPreorder);