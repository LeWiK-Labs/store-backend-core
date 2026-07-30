namespace LeWiK.Store.App.Payments;

public sealed class PaymentSettings
{
    // Public base URL of THIS backend (where Transbank redirects the buyer back).
    public string ReturnUrlBase { get; set; } = "";
    // Storefront page the buyer lands on after we process the result.
    public string StorefrontResultUrl { get; set; } = "";
    // Storefront page that handles guest payment links; the token is appended to it.
    public string StorefrontPayUrlBase { get; set; } = "";
}
