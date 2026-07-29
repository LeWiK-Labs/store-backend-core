namespace LeWiK.Store.App.Platform;

public sealed class TenancySettings
{
    // Platform domain that store slugs hang off: cardshop.lewik.app
    public string BaseDomain { get; set; } = "";

    // Dev convenience: resolve the tenant from X-Tenant-Id. MUST be false in production —
    // otherwise anyone could operate any store just by sending the header. Program.cs refuses
    // to start if this is true outside Development, because the failure is silent otherwise.
    public bool AllowHeaderOverride { get; set; }
}
