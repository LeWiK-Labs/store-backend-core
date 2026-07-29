using System.Security.Claims;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Platform;
using MediatR;

namespace LeWiK.Store.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Panel (staff) ---
        app.MapPost("/auth/staff/login", async (LoginBody body, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new LoginStaffCommand(
                body.Email, body.Password, http.Request.Headers.UserAgent.ToString()));
            if (result.IsFailure) return result.ToHttpResult();

            SetCookie(http, AuthCookies.Staff, result.Value.Token, result.Value.ExpiresAt);
            // The token goes in the cookie and nowhere else — not in the body, where a
            // logging proxy or a careless front-end would put it somewhere durable.
            return Results.Ok(new { result.Value.DisplayName, result.Value.Role, result.Value.ExpiresAt });
        });

        // Not behind a policy: logging out must work even from an already-dead session.
        // Requiring authorization would make a stale cookie impossible to clear.
        app.MapPost("/auth/staff/logout", async (HttpContext http, ISender sender) =>
        {
            var token = http.Request.Cookies[AuthCookies.Staff];
            if (!string.IsNullOrWhiteSpace(token))
                await sender.Send(new LogoutCommand(token, IsPlatform: false));
            ClearCookie(http, AuthCookies.Staff);
            return Results.NoContent();
        });

        app.MapGet("/auth/staff/me", (ClaimsPrincipal user) =>
                Results.Ok(new
                {
                    Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    Name = user.FindFirstValue(ClaimTypes.Name),
                    Role = user.FindFirstValue(ClaimTypes.Role),
                    TenantId = user.FindFirstValue(AuthClaims.TenantId),
                }))
            .RequireAuthorization(AuthPolicies.StoreStaff);

        // --- Platform (LeWiK) ---
        app.MapPost("/auth/platform/login", async (LoginBody body, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new LoginPlatformOperatorCommand(
                body.Email, body.Password, http.Request.Headers.UserAgent.ToString()));
            if (result.IsFailure) return result.ToHttpResult();

            SetCookie(http, AuthCookies.Platform, result.Value.Token, result.Value.ExpiresAt);
            return Results.Ok(new { result.Value.DisplayName, result.Value.ExpiresAt });
        });

        app.MapPost("/auth/platform/logout", async (HttpContext http, ISender sender) =>
        {
            var token = http.Request.Cookies[AuthCookies.Platform];
            if (!string.IsNullOrWhiteSpace(token))
                await sender.Send(new LogoutCommand(token, IsPlatform: true));
            ClearCookie(http, AuthCookies.Platform);
            return Results.NoContent();
        });

        app.MapGet("/auth/platform/me", (ClaimsPrincipal user) =>
                Results.Ok(new
                {
                    Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    Name = user.FindFirstValue(ClaimTypes.Name),
                }))
            .RequireAuthorization(AuthPolicies.PlatformOperator);

        return app;
    }

    // No Domain attribute, deliberately: the cookie stays host-only, so panel.<store> never
    // hands it to the storefront on www.<store>. Setting Domain=.tienda.cl would share one
    // panel credential with every subdomain, including whatever gets hosted there later.
    // HttpOnly keeps it out of JavaScript, so an XSS in the panel cannot read it.
    private static void SetCookie(HttpContext http, string name, string token, DateTime expiresAt) =>
        http.Response.Cookies.Append(name, token, new CookieOptions
        {
            HttpOnly = true,
            // Plain HTTP only survives on loopback; anywhere else the cookie is HTTPS-only.
            Secure = !IsLoopback(http.Request.Host.Host),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
        });

    // Since 4.4 the dev panel lives at panel.<slug>.localhost, not at localhost, and an exact
    // match here marked its cookie Secure — which no client stores over plain HTTP. Login
    // returned 200 and the session vanished. RFC 6761 reserves the whole .localhost TLD for
    // the loopback interface and browsers treat it as a secure context, which is precisely
    // what makes it usable for development; a production host can never end in it.
    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host is "127.0.0.1" or "[::1]";

    // Path must match the one it was set with, or the browser keeps the original cookie.
    private static void ClearCookie(HttpContext http, string name) =>
        http.Response.Cookies.Delete(name, new CookieOptions { Path = "/" });
}

public sealed record LoginBody(string Email, string Password);
