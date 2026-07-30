using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;

namespace LeWiK.Store.App.Orders;

public static class OrderErrors
{
    public static Error EmptyOrder() =>
        Error.Validation("order.empty", "An order must have at least one line.");
    public static Error MixedCurrency() =>
        Error.Validation("order.mixed_currency", "All order lines must share the same currency.");
    public static Error OrderCancelled() =>
        Error.Conflict("order.cancelled", "The order is cancelled.");
    public static Error AlreadyPaid() =>
        Error.Conflict("order.already_paid", "The order is already fully paid.");
    public static Error PaymentExceedsBalance(decimal amount, decimal balance) =>
        Error.Conflict("order.payment_exceeds_balance", $"Payment {amount} exceeds balance {balance}.");
    public static Error RefundExceedsPaid(decimal amount, decimal refundable) =>
        Error.Conflict("order.refund_exceeds_paid", $"Refund {amount} exceeds the refundable {refundable} on this order.");
    public static Error BalancePending() =>
        Error.Conflict("order.balance_pending", "The order still has a pending balance.");
    // Not an error a human can cause: only the expiry worker asks for a conditional cancel, and
    // this is the answer when the order stopped qualifying between the candidate query and the
    // cancel — it got paid, or someone cancelled it first.
    public static Error ReservationNotExpired(Guid orderId) =>
        Error.Conflict("order.reservation_not_expired",
            $"Order {orderId} no longer has a lapsed reservation.");
    public static Error CannotCancelDelivered() =>
        Error.Conflict("order.cannot_cancel_delivered", "A delivered order cannot be cancelled.");
    public static Error LineNotFound(Guid lineId) =>
        Error.NotFound("order.line_not_found", $"Order line {lineId} was not found.");
    public static Error FulfillExceedsPending(string sku, int qty, int pending) =>
        Error.Conflict("order.fulfill_exceeds_pending", $"Cannot fulfill {qty} of '{sku}'; only {pending} pending.");
    public static Error InvalidTransition(FulfillmentStatus from, string action) =>
        Error.Conflict("order.invalid_transition", $"Cannot '{action}' from status '{from}'.");
    public static Error VariantNotFound(Guid variantId) =>
        Error.NotFound("order.variant_not_found", $"Variant {variantId} was not found.");
    public static Error VariantHasNoStock(Guid variantId) =>
        Error.Conflict("order.no_stock", $"Variant {variantId} has no inventory to sell from.");
    public static Error PaymentLinkInvalid() =>
        Error.NotFound("order.payment_link_invalid", "This payment link is invalid or has expired.");
    public static Error NothingToPay() =>
        Error.Conflict("order.nothing_to_pay", "This order has no pending balance.");
    public static Error OrderNotFound(Guid orderId) =>
        Error.NotFound("order.not_found", $"Order {orderId} was not found.");
    public static Error PreorderStockMissing(string sku) =>
        Error.Conflict("order.preorder_stock_missing",
            $"Cannot release '{sku}': its stock has not arrived yet. Add stock for the variant first.");
}