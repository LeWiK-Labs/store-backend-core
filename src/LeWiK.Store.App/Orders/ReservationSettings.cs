namespace LeWiK.Store.App.Orders;

public sealed class ReservationSettings
{
    // Off means orders never get a deadline and the sweeper does not run. Existing windows are
    // left alone rather than cleared: turning the feature off should not silently make orders
    // that were already on the clock immortal in the database and confusing in the panel.
    public bool Enabled { get; set; } = true;

    // How long an unpaid order holds its stock/preorder capacity.
    public int TtlMinutes { get; set; } = 30;

    // Extra time granted when the buyer starts paying at a gateway, so the sweeper can't cancel
    // the order while they're entering their card.
    public int PaymentGraceMinutes { get; set; } = 20;

    public int SweepIntervalSeconds { get; set; } = 60;

    // Cap per sweep. The batch is not a backlog limit — whatever is left over is picked up on
    // the next tick — it bounds how long one sweep holds scopes and connections open.
    public int BatchSize { get; set; } = 100;
}
