namespace LeWiK.Store.App.Orders.Domain;

public enum FulfillmentStatus
{
    PendingPayment, // waiting for payment to clear
    AwaitingRelease, // paid/deposited but has preorder lines awaiting stock
    Paid, // fully paid, ready to prepare
    Preparing, // being prepared
    PartiallyDelivered, // some lines handed over, not all
    Delivered, // all lines handed over
    Cancelled
}

public enum PaymentStatus
{
    Pending,   // nothing paid
    Deposited, // partial (abono) paid
    Paid       // fully paid
}