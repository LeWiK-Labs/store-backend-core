using System.Text.Json;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.App.Platform;

public sealed record ResolvedStore(Guid TenantId, string Slug, bool IsActive);

// What a hostname is asking for, once the platform's own prefixes are stripped: either a
// slug (a subdomain of the base domain) or the store's own domain, in the spellings worth
// trying. Kept as a value so the parsing can be tested without a database — it is where
// all the actual logic of this class lives.
public readonly record struct HostLookup(string? Slug, string[] Domains)
{
    public static readonly HostLookup None = new(null, []);
    public bool IsEmpty => Slug is null && Domains.Length == 0;
}

// Maps an incoming host to a store. Runs on every request, so results are cached
// with a short TTL plus explicit invalidation when a store changes.
public sealed class StoreResolver(
    StoreDbContext db, IDistributedCache cache, IOptions<TenancySettings> settings)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Subdomains that are never a store: they belong to the platform itself. Also the set of
    // prefixes stripped before resolution — "panel." is front-end routing, not identity, so
    // panel.cardshop.cl and cardshop.cl are the same store as far as the backend is concerned.
    private static readonly string[] Reserved =
        ["www", "panel", "api", "admin", "app", "static", "cdn"];

    private static bool IsReserved(string label) =>
        Reserved.Contains(label, StringComparer.OrdinalIgnoreCase);

    public static string CacheKey(string host) => $"tenant:host:{host.ToLowerInvariant()}";

    // Every host spelling that could hold a cached entry for this store. Derived from the same
    // Reserved set the parser strips, so adding a prefix there cannot leave a stale key behind
    // here — the enumeration and the stripping can no longer drift apart.
    public static IEnumerable<string> CacheKeysFor(string slug, string? customDomain, string baseDomain)
    {
        if (!string.IsNullOrWhiteSpace(baseDomain))
            foreach (var key in Spellings($"{slug}.{baseDomain}")) yield return key;

        if (!string.IsNullOrWhiteSpace(customDomain))
            foreach (var key in Spellings(customDomain)) yield return key;

        static IEnumerable<string> Spellings(string host)
        {
            yield return CacheKey(host);
            foreach (var prefix in Reserved) yield return CacheKey($"{prefix}.{host}");
        }
    }

    public async Task<ResolvedStore?> ResolveAsync(string host, CancellationToken ct = default)
    {
        var normalized = Normalize(host);
        if (normalized.Length == 0) return null;

        var key = CacheKey(normalized);

        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null)
            return cached.Length == 0 ? null : JsonSerializer.Deserialize<ResolvedStore>(cached);

        var resolved = await LookUpAsync(ParseHost(normalized, settings.Value.BaseDomain), ct);

        // Cache misses too (empty string): stops unknown hosts hammering the database.
        await cache.SetStringAsync(key,
            resolved is null ? "" : JsonSerializer.Serialize(resolved),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);

        return resolved;
    }

    // Lowercase, no port, no root dot. Hostnames are case-insensitive and "cardshop.cl." is
    // the same name as "cardshop.cl" — two spellings must not become two cache entries.
    public static string Normalize(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";

        // Strip a trailing :port without mangling an IPv6 literal, whose colons are part of
        // the address. Request.Host.Host already drops the port; this is for direct callers.
        var portAt = host.LastIndexOf(':');
        if (portAt > 0 && host.IndexOf(']', portAt) < 0) host = host[..portAt];

        return host.Trim().TrimEnd('.').ToLowerInvariant();
    }

    // Pure: no database, no cache. Expects an already-normalized host.
    public static HostLookup ParseHost(string host, string baseDomain)
    {
        if (host.Length == 0) return HostLookup.None;
        baseDomain = baseDomain?.ToLowerInvariant().TrimEnd('.') ?? "";

        // Case A: subdomain of the platform base domain -> resolve by slug.
        if (baseDomain.Length > 0 && host.EndsWith($".{baseDomain}", StringComparison.Ordinal))
        {
            var prefix = host[..^(baseDomain.Length + 1)];              // "panel.cardshop"
            var labels = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);

            // Only two shapes exist under the base domain: slug.base and panel.slug.base.
            // Anything deeper is not something we serve, and guessing which label is the slug
            // would resolve an unsupported host to a real store by accident.
            var slug = labels.Length switch
            {
                1 => labels[0],
                2 when IsReserved(labels[0]) => labels[1],
                _ => null
            };

            // The platform's own subdomains are not stores: www.lewik.app is not a slug.
            return slug is null || IsReserved(slug) ? HostLookup.None : new HostLookup(slug, []);
        }

        // Case B: the store's own domain. Try the host as-is and, when it starts with one of
        // our prefixes, without it — so all four shapes land on the same record whether the
        // store registered "cardshop.cl" or "www.cardshop.cl".
        var firstDot = host.IndexOf('.');
        var bare = firstDot > 0 && IsReserved(host[..firstDot]) ? host[(firstDot + 1)..] : null;

        return new HostLookup(null, bare is null ? [host] : [host, bare]);
    }

    private async Task<ResolvedStore?> LookUpAsync(HostLookup lookup, CancellationToken ct)
    {
        if (lookup.IsEmpty) return null;

        var stores = db.Set<Domain.Store>().AsNoTracking();

        // Both columns are uniquely indexed, so at most one row can match either way.
        var match = lookup.Slug is { } slug
            ? stores.Where(s => s.Slug == slug)
            : stores.Where(s => s.CustomDomain != null && lookup.Domains.Contains(s.CustomDomain));

        return await match
            .Select(s => new ResolvedStore(s.Id, s.Slug, s.Status == StoreStatus.Active))
            .FirstOrDefaultAsync(ct);
    }
}
