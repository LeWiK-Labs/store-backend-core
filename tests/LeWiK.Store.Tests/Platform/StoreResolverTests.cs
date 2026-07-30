using LeWiK.Store.App.Platform;

namespace LeWiK.Tienda.Tests.Platform;

// StoreResolver's database half is one indexed lookup; everything that can actually go wrong
// lives in turning a Host header into what to look up. That part is pure, so it is tested here
// rather than through the API.
public class StoreResolverTests
{
    private const string Base = "lewik.app";

    // ---- the four shapes that must all reach the same store ------------------------------

    [Theory]
    [InlineData("cardshop.lewik.app")]        // storefront
    [InlineData("panel.cardshop.lewik.app")]  // back-office; the prefix is front-end routing
    [InlineData("www.cardshop.lewik.app")]
    public void A_subdomain_of_the_base_domain_resolves_by_slug(string host)
    {
        var lookup = StoreResolver.ParseHost(host, Base);

        Assert.Equal("cardshop", lookup.Slug);
        Assert.Empty(lookup.Domains);
    }

    [Theory]
    // The store may have registered either spelling, so both are tried and the bare form is
    // offered as a fallback. That is what makes www./panel. optional on a custom domain.
    [InlineData("cardshop.cl", new[] { "cardshop.cl" })]
    [InlineData("www.cardshop.cl", new[] { "www.cardshop.cl", "cardshop.cl" })]
    [InlineData("panel.cardshop.cl", new[] { "panel.cardshop.cl", "cardshop.cl" })]
    public void A_foreign_host_resolves_by_custom_domain(string host, string[] expected)
    {
        var lookup = StoreResolver.ParseHost(host, Base);

        Assert.Null(lookup.Slug);
        Assert.Equal(expected, lookup.Domains);
    }

    // ---- hosts that must resolve to nothing ----------------------------------------------

    [Theory]
    [InlineData("www.lewik.app")]     // the platform's marketing site
    [InlineData("panel.lewik.app")]
    [InlineData("api.lewik.app")]
    [InlineData("app.lewik.app")]
    public void The_platforms_own_subdomains_are_not_stores(string host)
    {
        // Otherwise registering the slug "api" would hand someone the platform's own hostname.
        Assert.True(StoreResolver.ParseHost(host, Base).IsEmpty);
    }

    [Theory]
    [InlineData("a.b.cardshop.lewik.app")]   // deeper than anything we serve
    [InlineData("shop.cardshop.lewik.app")]  // unknown prefix: not ours to interpret
    public void Unsupported_shapes_under_the_base_domain_resolve_to_nothing(string host)
    {
        // Guessing which label is the slug would silently point an unsupported host at a real
        // store. Under the base domain the answer is the set of shapes we publish, or nothing.
        Assert.True(StoreResolver.ParseHost(host, Base).IsEmpty);
    }

    [Fact]
    public void Without_a_base_domain_only_custom_domains_resolve()
    {
        // A missing Tenancy:BaseDomain must not silently turn every host into a slug lookup.
        var lookup = StoreResolver.ParseHost("cardshop.lewik.app", "");

        Assert.Null(lookup.Slug);
        Assert.Equal(["cardshop.lewik.app"], lookup.Domains);
    }

    // ---- normalisation: one host must not become two cache entries -----------------------

    [Theory]
    [InlineData("CardShop.LEWIK.app", "cardshop.lewik.app")]  // hostnames are case-insensitive
    [InlineData("cardshop.lewik.app.", "cardshop.lewik.app")] // the root dot is legal and equal
    [InlineData("cardshop.localhost:5223", "cardshop.localhost")]
    [InlineData(" cardshop.cl ", "cardshop.cl")]
    [InlineData("", "")]
    public void Hosts_are_normalised_before_anything_else(string given, string expected) =>
        Assert.Equal(expected, StoreResolver.Normalize(given));

    [Theory]
    [InlineData("[::1]:5223", "[::1]")]
    [InlineData("[::1]", "[::1]")]
    public void An_ipv6_literal_keeps_its_colons(string given, string expected) =>
        // Splitting on the first colon would turn "[::1]" into "[" — no store either way, but
        // the cache key and the debug log would name a host that was never requested.
        Assert.Equal(expected, StoreResolver.Normalize(given));

    [Fact]
    public void Case_survives_normalisation_all_the_way_to_the_slug() =>
        Assert.Equal("cardshop",
            StoreResolver.ParseHost(StoreResolver.Normalize("PANEL.CardShop.LEWIK.App"), Base).Slug);

    // ---- the invariant that keeps invalidation honest -------------------------------------

    [Fact]
    public void Every_host_that_resolves_to_a_store_has_its_key_in_that_stores_key_set()
    {
        // Suspension drops keys by enumerating spellings. If the parser ever accepts a spelling
        // the enumeration does not produce, a suspended store keeps serving from cache until
        // the TTL expires. This is the test that fails when the two drift apart.
        var keys = StoreResolver.CacheKeysFor("cardshop", "cardshop.cl", Base).ToHashSet();

        string[] hosts =
        [
            "cardshop.lewik.app", "panel.cardshop.lewik.app", "www.cardshop.lewik.app",
            "cardshop.cl", "www.cardshop.cl", "panel.cardshop.cl"
        ];

        foreach (var host in hosts)
            Assert.Contains(StoreResolver.CacheKey(host), keys);
    }

    [Fact]
    public void A_store_without_a_custom_domain_only_claims_base_domain_keys()
    {
        var keys = StoreResolver.CacheKeysFor("cardshop", null, Base).ToList();

        Assert.Contains(StoreResolver.CacheKey("panel.cardshop.lewik.app"), keys);
        Assert.All(keys, k => Assert.Contains(Base, k));
    }
}
