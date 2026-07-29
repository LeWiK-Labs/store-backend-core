using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace LeWiK.Store.Api.Auth;

public static class AuthPolicies
{
    public const string PlatformOperator = "PlatformOperator";
    public const string StoreStaff = "StoreStaff";      // any staff of THIS store
    public const string StoreAdmin = "StoreAdmin";      // owner or admin
    public const string StoreOwner = "StoreOwner";      // owner only (gateways, billing)
}

public static class AuthSetup
{
    public static IServiceCollection AddStoreAuth(this IServiceCollection services)
    {
        // Two schemes, so a credential from one population can never satisfy the other's
        // policy: each reads its own cookie and its own table. There is no shared path where
        // a staff token could be mistaken for an operator's.
        services.AddAuthentication(AuthSchemes.Staff)
            .AddScheme<AuthenticationSchemeOptions, StaffSessionHandler>(AuthSchemes.Staff, null)
            .AddScheme<AuthenticationSchemeOptions, PlatformSessionHandler>(AuthSchemes.Platform, null);

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
                .AddRequirements(new TenantMatchRequirement()));

        return services;
    }
}
