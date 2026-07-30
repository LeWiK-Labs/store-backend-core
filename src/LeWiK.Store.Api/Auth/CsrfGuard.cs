namespace LeWiK.Store.Api.Auth;

// Defence in depth over SameSite=Lax and a strict CORS allow-list, not a replacement for
// either. ASP.NET's antiforgery is built for HTML forms; for a JSON API the protection already
// comes from three places: Lax keeps the cookie off cross-site POSTs, application/json is not
// a CORS-simple content type so it forces a preflight, and the preflight only passes for the
// configured origins.
//
// This adds a fourth, cheap layer: a custom header a cross-site page cannot set without an
// approved preflight it will never get. The panel sends it on every call.
public static class CsrfGuard
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "LeWiKPanel";

    // Generic over the builder so it chains onto both a single endpoint and a whole
    // MapGroup — the admin surface is grouped, and applying this per-handler is exactly how
    // one gets forgotten.
    public static TBuilder RequireCsrfHeader<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;

            // Safe methods change nothing, and requiring the header on them would break
            // ordinary navigation to any of these URLs.
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
                return await next(context);

            return http.Request.Headers[HeaderName] == HeaderValue
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "request.csrf_header_missing",
                    detail: $"State-changing panel calls must send {HeaderName}: {HeaderValue}.");
        });
}
