namespace LeWiK.Store.Api.Auth;

// One place that decides how a session cookie is written, shared by all three populations.
// Extracted when customers arrived: three copies of these flags would be three chances for one
// of them to drift, and the flags are the whole security posture of the credential.
internal static class SessionCookies
{
    // No Domain attribute, deliberately: the cookie stays host-only, so panel.<store> never
    // hands it to the storefront on www.<store>, and vice versa. Setting Domain=.tienda.cl
    // would share one credential with every subdomain, including whatever gets hosted there
    // later. HttpOnly keeps it out of JavaScript, so an XSS cannot read it.
    public static void Set(HttpContext http, string name, string token, DateTime expiresAt) =>
        http.Response.Cookies.Append(name, token, new CookieOptions
        {
            HttpOnly = true,
            // Plain HTTP only survives on loopback; anywhere else the cookie is HTTPS-only.
            Secure = !IsLoopback(http.Request.Host.Host),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
        });

    // Path must match the one it was set with, or the browser keeps the original cookie.
    public static void Clear(HttpContext http, string name) =>
        http.Response.Cookies.Delete(name, new CookieOptions { Path = "/" });

    // Since 4.4 the dev panel lives at panel.<slug>.localhost and the storefront at
    // <slug>.localhost, not at localhost, and an exact match here marked their cookies Secure —
    // which no client stores over plain HTTP. Login returned 200 and the session vanished.
    // RFC 6761 reserves the whole .localhost TLD for the loopback interface and browsers treat
    // it as a secure context, which is what makes it usable for development; a production host
    // can never end in it.
    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host is "127.0.0.1" or "[::1]";
}
