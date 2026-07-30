using System.Security.Claims;
using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Customers;
using MediatR;

namespace LeWiK.Store.Api.Customers;

public static class CustomerAuthEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Public by necessity: this is where a buyer with no credential gets one. The store
        // comes from the domain, so registering on cardshop.cl creates a cardshop customer.
        app.MapPost("/auth/customer/register", async (RegisterBody body, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new RegisterCustomerCommand(
                body.Email, body.Password, body.Phone, body.Name, http.Request.Headers.UserAgent.ToString()));
            if (result.IsFailure) return result.ToHttpResult();

            SessionCookies.Set(http, AuthCookies.Customer, result.Value.Token, result.Value.ExpiresAt);
            // The token goes in the cookie and nowhere else — not in the body, where a logging
            // proxy or a careless front-end would put it somewhere durable.
            return Results.Ok(new { result.Value.Name, result.Value.Email, result.Value.ExpiresAt });
        });

        app.MapPost("/auth/customer/login", async (LoginBody body, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new LoginCustomerCommand(
                body.Email, body.Password, http.Request.Headers.UserAgent.ToString()));
            if (result.IsFailure) return result.ToHttpResult();

            SessionCookies.Set(http, AuthCookies.Customer, result.Value.Token, result.Value.ExpiresAt);
            return Results.Ok(new { result.Value.Name, result.Value.Email, result.Value.ExpiresAt });
        });

        // Not behind a policy: logging out must work even from an already-dead session, or a
        // stale cookie becomes impossible to clear.
        app.MapPost("/auth/customer/logout", async (HttpContext http, ISender sender) =>
        {
            var token = http.Request.Cookies[AuthCookies.Customer];
            if (!string.IsNullOrWhiteSpace(token))
                await sender.Send(new LogoutCustomerCommand(token));
            SessionCookies.Clear(http, AuthCookies.Customer);
            return Results.NoContent();
        });

        // --- Account area ---
        // Everything here is read-only and scoped by the session's own customer id, so there is
        // no CSRF surface: the guard passes GETs anyway, and no route here changes state.
        var account = app.MapGroup("/account").RequireAuthorization(AuthPolicies.Customer);

        account.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            Name = user.FindFirstValue(ClaimTypes.Name),
        }));

        account.MapGet("/orders", async (ClaimsPrincipal user, ISender sender) =>
            (await sender.Send(new ListMyOrdersQuery(CustomerId(user)))).ToHttpResult());

        account.MapGet("/orders/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, ISender sender) =>
            (await sender.Send(new GetMyOrderQuery(CustomerId(user), orderId))).ToHttpResult());

        return app;
    }

    // From the claims, never from the request. The policy has already established that this
    // claim exists and belongs to a live session of this store.
    private static Guid CustomerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record RegisterBody(string Email, string Password, string Phone, string? Name);
