using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// The security review of #114 (PR-D): published, the edge serves the web app to browsers, so what it answers tells the
/// browser how to treat it. Every answer is <c>X-Content-Type-Options: nosniff</c> with a <c>Referrer-Policy</c> that sends
/// no path to another origin; <c>index.html</c> carries a <c>Content-Security-Policy</c> that runs scripts from the edge's
/// own origin and the page's inline script by its hash alone (never <c>'unsafe-inline'</c> or <c>'unsafe-eval'</c>), loads
/// nothing else from anywhere but the edge, lets the browser call the identity provider's origin (its token endpoint), and
/// lets no other page frame the app. In Production, as the Container Apps ingress forwards an HTTPS request, the edge also
/// answers <c>Strict-Transport-Security</c> for at least a year; in Development it never does.
/// </summary>
public class SecurityHeadersTests
{
    /// <summary>The origin of <see cref="EdgeHost.Authority"/>: where the browser fetches the realm's metadata and tokens.</summary>
    private const string AuthorityOrigin = "https://auth.edge-tests.example";

    /// <summary>Blob storage's endpoint as Aspire gives it, with a trailing slash; the policy names its origin alone.</summary>
    private const string BlobEndpoint = "https://store.blob.core.windows.net/";

    private const string BlobOrigin = "https://store.blob.core.windows.net";

    /// <summary>The policies that send another origin no path: the default, strict-origin-when-cross-origin, or stricter.</summary>
    private static readonly string[] StrictReferrerPolicies = ["no-referrer", "same-origin", "strict-origin", "strict-origin-when-cross-origin"];

    /// <summary>Script sources that would let a script injected into the page, or fetched from anywhere, run.</summary>
    private static readonly string[] UnsafeScriptSources = ["'unsafe-inline'", "'unsafe-eval'", "'unsafe-hashes'", "*", "data:", "blob:", "http:", "https:"];

    [Theory]
    [InlineData("/")]
    [InlineData("/catalog")]
    public async Task The_app_is_answered_with_nosniff_and_a_strict_referrer_policy(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation(path), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync($"A page load of {path}");
        ShouldBeNosniffWithAStrictReferrerPolicy(response, $"A page load of {path}");
    }

    [Fact]
    public async Task A_file_of_the_web_root_is_answered_with_nosniff_and_a_strict_referrer_policy()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.File(WebRoot.AssetPath), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, $"GET {WebRoot.AssetPath} answered {(int)response.StatusCode}.");
        ShouldBeNosniffWithAStrictReferrerPolicy(response, $"GET {WebRoot.AssetPath}");
    }

    [Theory]
    [InlineData("/config.json")]
    [InlineData("/catalog/samples")]
    public async Task Data_is_answered_with_nosniff(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Data(path), TestContext.Current.CancellationToken);

        await response.ShouldNotBeTheAppAsync($"A call for data to {path}");
        Values(response, "X-Content-Type-Options").ShouldContain(
            "nosniff",
            $"A call for data to {path} answered {(int)response.StatusCode} without X-Content-Type-Options: nosniff; a browser may read JSON as a script or a page. Headers: {AllHeaders(response)}");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/catalog")]
    public async Task The_app_carries_a_content_security_policy_that_runs_only_its_own_scripts(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation(path), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync($"A page load of {path}");
        var policy = string.Join(", ", Values(response, "Content-Security-Policy"));
        policy.ShouldNotBeNullOrWhiteSpace($"A page load of {path} answered index.html without a Content-Security-Policy. Headers: {AllHeaders(response)}");
        var directives = Directives(policy);
        List<string> problems = [];

        Expect(directives, "default-src", ["'self'"], policy, problems);
        Expect(directives, "object-src", ["'none'"], policy, problems);
        Expect(directives, "frame-ancestors", ["'none'"], policy, problems);
        Expect(directives, "base-uri", ["'self'"], policy, problems);

        var scripts = directives.GetValueOrDefault("script-src") ?? directives.GetValueOrDefault("default-src") ?? [];
        if (!scripts.Contains("'self'"))
        {
            problems.Add($"script-src should allow 'self', where the app's /assets/*.js come from; it is [{string.Join(" ", scripts)}].");
        }

        var hash = $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(WebRoot.InlineScript)))}'";
        if (!scripts.Contains(hash))
        {
            problems.Add($"script-src should allow index.html's inline script by its hash, {hash} (the hash of every inline <script> of the index.html it serves), or the theme and language are never set before the first paint; it is [{string.Join(" ", scripts)}].");
        }

        foreach (var source in scripts.Where(source => UnsafeScriptSources.Contains(source, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"script-src allows {source}: a script injected into the page, or fetched from anywhere, would run.");
        }

        var connections = directives.GetValueOrDefault("connect-src") ?? [];
        foreach (var source in new[] { "'self'", AuthorityOrigin })
        {
            if (!connections.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"connect-src should allow {source}: the app calls the edge, and the identity provider's discovery and token endpoints at {AuthorityOrigin} (Authentication:Authority's origin); it is [{string.Join(" ", connections)}].");
            }
        }

        if (connections.Contains("*") || connections.Contains("https:", StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"connect-src lets the page send data anywhere: [{string.Join(" ", connections)}].");
        }

        problems.ShouldBeEmpty($"A page load of {path} answered Content-Security-Policy: {policy}");
    }

    /// <summary>
    /// Published, the AppHost hands the edge blob storage's endpoint as <c>Edge:BlobEndpoint</c>; the browser uploads a
    /// chapter's file there with a <c>PUT</c> to a signed URL, which <c>connect-src</c> must allow by the endpoint's origin.
    /// </summary>
    [Fact]
    public async Task The_page_may_call_blob_storages_origin_when_the_edge_is_given_one()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["Edge:BlobEndpoint"] = BlobEndpoint };
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production, settings);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation("/"), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync("A page load of /");
        var policy = string.Join(", ", Values(response, "Content-Security-Policy"));
        var connections = Directives(policy).GetValueOrDefault("connect-src") ?? [];
        connections.ShouldBe(
            ["'self'", AuthorityOrigin, BlobOrigin],
            ignoreOrder: true,
            $"With Edge:BlobEndpoint {BlobEndpoint}, connect-src should allow the edge, the identity provider and blob storage's origin {BlobOrigin} (no path), or the browser refuses to upload a file; Content-Security-Policy: {policy}");
    }

    [Fact]
    public async Task The_page_may_call_no_other_origin_when_the_edge_is_given_no_blob_storage()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation("/"), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync("A page load of /");
        var policy = string.Join(", ", Values(response, "Content-Security-Policy"));
        var connections = Directives(policy).GetValueOrDefault("connect-src") ?? [];
        connections.ShouldBe(
            ["'self'", AuthorityOrigin],
            ignoreOrder: true,
            $"Without Edge:BlobEndpoint, connect-src should allow the edge and the identity provider alone; Content-Security-Policy: {policy}");
    }

    /// <summary>The editorial module reads an upload's duration by playing the file from a <c>blob:</c> object URL.</summary>
    [Fact]
    public async Task The_page_may_play_video_from_its_own_blob_urls_alone()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(Requests.Navigation("/"), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync("A page load of /");
        var policy = string.Join(", ", Values(response, "Content-Security-Policy"));
        List<string> problems = [];
        Expect(Directives(policy), "media-src", ["'self'", "blob:"], policy, problems);
        problems.ShouldBeEmpty($"media-src should allow blob:, or reading an upload's duration fails; Content-Security-Policy: {policy}");
    }

    [Theory]
    [InlineData("/")]
    [InlineData(WebRoot.AssetPath)]
    public async Task Production_asks_the_browser_to_use_https_alone(string path)
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Production);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(AsTheIngressForwardsIt(path), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, $"GET {path}, forwarded from HTTPS, answered {(int)response.StatusCode}.");
        var transport = string.Join(", ", Values(response, "Strict-Transport-Security"));
        transport.ShouldNotBeNullOrWhiteSpace($"In Production, GET {path} as the Container Apps ingress forwards it (plain HTTP to the edge, X-Forwarded-Proto: https) answered no Strict-Transport-Security. Headers: {AllHeaders(response)}");
        var maxAge = transport.Split(';', StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(part => part.Length == 2 && part[0].Equals("max-age", StringComparison.OrdinalIgnoreCase))
            .Select(part => long.TryParse(part[1], out var seconds) ? seconds : -1)
            .FirstOrDefault(-1);
        maxAge.ShouldBeGreaterThanOrEqualTo((long)TimeSpan.FromDays(365).TotalSeconds, $"Strict-Transport-Security should keep HTTPS for at least a year; it is '{transport}'.");
    }

    [Fact]
    public async Task Development_never_asks_for_https_alone()
    {
        await using var edge = await EdgeHost.ServingTheAppAsync(Environments.Development);
        using var client = edge.CreateClient();

        using var response = await client.SendAsync(AsTheIngressForwardsIt("/"), TestContext.Current.CancellationToken);

        await response.ShouldBeTheAppAsync("A page load of / in Development");
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse(
            $"In Development the edge answered Strict-Transport-Security '{string.Join(", ", Values(response, "Strict-Transport-Security"))}'; a browser would refuse plain HTTP to that host from then on.");
    }

    private static void ShouldBeNosniffWithAStrictReferrerPolicy(HttpResponseMessage response, string request)
    {
        Values(response, "X-Content-Type-Options").ShouldContain(
            "nosniff",
            $"{request} answered without X-Content-Type-Options: nosniff. Headers: {AllHeaders(response)}");
        var referrer = Values(response, "Referrer-Policy").SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).LastOrDefault();
        referrer.ShouldNotBeNull($"{request} answered no Referrer-Policy. Headers: {AllHeaders(response)}");
        StrictReferrerPolicies.ShouldContain(referrer, $"{request} answered Referrer-Policy '{referrer}'; it should be strict-origin-when-cross-origin or stricter ({string.Join(", ", StrictReferrerPolicies)}).");
    }

    /// <summary>What the Container Apps ingress sends the edge for a browser's HTTPS request: plain HTTP, with X-Forwarded-Proto.</summary>
    private static HttpRequestMessage AsTheIngressForwardsIt(string path)
    {
        var request = path.StartsWith("/assets/", StringComparison.Ordinal) ? Requests.File(path) : Requests.Navigation(path);
        request.RequestUri = new Uri(new Uri("http://host.edge-tests.example"), path);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        return request;
    }

    private static void Expect(Dictionary<string, string[]> directives, string name, string[] expected, string policy, List<string> problems)
    {
        if (!directives.TryGetValue(name, out var sources) || !sources.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"{name} should be {string.Join(" ", expected)}; it is {(sources is null ? "missing" : $"[{string.Join(" ", sources)}]")}.");
        }
    }

    /// <summary>A policy's directives by name, each with its sources; the first of a repeated directive counts, as in a browser.</summary>
    private static Dictionary<string, string[]> Directives(string policy)
    {
        var directives = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var directive in policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = directive.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            directives.TryAdd(parts[0], parts[1..]);
        }

        return directives;
    }

    private static List<string> Values(HttpResponseMessage response, string header) =>
        [.. response.Headers.TryGetValues(header, out var values) ? values : [],
            .. response.Content.Headers.TryGetValues(header, out var contentValues) ? contentValues : []];

    private static string AllHeaders(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.Concat(response.Content.Headers).Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
}
