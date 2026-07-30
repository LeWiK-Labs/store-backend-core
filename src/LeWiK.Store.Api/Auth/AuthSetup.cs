using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace LeWiK.Store.Api.Auth;

public static class AuthPolicies
{
    public const string PlatformOperator = "PlatformOperator";
    public const string StoreStaff = "StoreStaff";      // any staff of THIS store
    public const string StoreAdmin = "StoreAdmin";      // owner or admin
    public const string StoreOwner = "StoreOwner";      // owner only (gateways, billing)
    public const string Customer = "Customer";          // a buyer of THIS store
}

public static class AuthSetup
{
    public static IServiceCollection AddStoreAuth(this IServiceCollection services)
    {
        // Three schemes, so a credential from one population can never satisfy another's
        // policy: each reads its own cookie and its own table. There is no shared path where a
        // staff token could be mistaken for an operator's, or a buyer's for either.
        services.AddAuthentication(AuthSchemes.Staff)
            .AddScheme<AuthenticationSchemeOptions, StaffSessionHandler>(AuthSchemes.Staff, null)
            .AddScheme<AuthenticationSchemeOptions, PlatformSessionHandler>(AuthSchemes.Platform, null)
            .AddScheme<AuthenticationSchemeOptions, CustomerSessionHandler>(AuthSchemes.Customer, null);

        services.AddScoped<IAuthorizationHandler, TenantMatchHandler>();

        // Every store policy carries TenantMatchRequirement. Roles alone are not enough:
        // "is an Owner" is worthless without "of THIS store".
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.PlatformOperator, p => p
                .AddAuthenticationSchemes(AuthSchemes.Platform)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.StoreStaff, p => p
                .AddAuthenticationSchemes(AuthSchemes.Staff)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantMatchRequirement()))
            .AddPolicy(AuthPolicies.StoreAdmin, p => p
                .AddAuthenticationSchemes(AuthSchemes.Staff)
                .RequireAuthenticatedUser()
                .RequireRole("Owner", "Admin")
                .AddRequirements(new TenantMatchRequirement()))
            .AddPolicy(AuthPolicies.StoreOwner, p => p
                .AddAuthenticationSchemes(AuthSchemes.Staff)
                .RequireAuthenticatedUser()
                .RequireRole("Owner")
                .AddRequirements(new TenantMatchRequirement()))
            // Buyers get the same tenant check as staff: a customer of store A must not read
            // store B's orders by pointing their browser at B's domain.
            .AddPolicy(AuthPolicies.Customer, p => p
                .AddAuthenticationSchemes(AuthSchemes.Customer)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantMatchRequirement()));

        return services;
    }
}
