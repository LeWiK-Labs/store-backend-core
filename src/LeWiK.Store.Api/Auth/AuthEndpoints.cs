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

    // Cookie flags live in SessionCookies, shared with the customer endpoints.
    private static void SetCookie(HttpContext http, string name, string token, DateTime expiresAt) =>
        SessionCookies.Set(http, name, token, expiresAt);

    private static void ClearCookie(HttpContext http, string name) =>
        SessionCookies.Clear(http, name);
}

public sealed record LoginBody(string Email, string Password);
