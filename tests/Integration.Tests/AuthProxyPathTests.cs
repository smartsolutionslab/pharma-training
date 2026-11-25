using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// #462: the published <c>auth</c> proxy (<c>src/AppHost/Wiring/AuthProxy.cs</c>) forwards the realm
/// <c>pharma-training</c> and the static resources to Keycloak and nothing else, and that holds for what a client really
/// sends, not only for the route templates <see cref="AppHostKeycloakTests"/> reads. The proxy runs here as YARP on real
/// Kestrel, which normalises the path as the published container does, with the routes production's model configures,
/// in front of run mode's Keycloak; each variant of an admin or master-realm path goes out unchanged, percent-encoded,
/// with dot segments, doubled slashes or another case, carrying the master administrator's token. A variant that reaches
/// Keycloak's admin API answers 200 with that token, or 401 or 403 if Keycloak judges it; one the proxy refuses answers
/// 404 (or 400).
/// </summary>
public class AuthProxyPathTests(AspireFixture aspire)
{
    private const string Auth = "auth";

    private const string ReverseProxy = "REVERSEPROXY__";

    private static readonly string[] Variants =
    [
        // At the root, outside every route.
        "/%2Fadmin/realms/pharma-training/users",
        "/%61dmin/realms/pharma-training/users",
        "/./admin/realms/pharma-training/users",
        "/realms/../admin/realms/pharma-training/users",
        "//admin/realms/pharma-training/users",
        "/Admin/realms/pharma-training/users",

        // Inside the realm's or the resources' route, escaping it once Keycloak reads the path.
        "/realms/pharma-training/..%2F..%2Fadmin/realms/pharma-training/users",
        "/realms/pharma-training/..;/..;/admin/realms/pharma-training/users",
        "/realms/pharma-training/%2e%2e/%2e%2e/admin/realms/pharma-training/users",
        "/realms/pharma-training/%2e%2e%2f%2e%2e%2fadmin/realms/pharma-training/users",
        "/realms/PHARMA-TRAINING/..%2F..%2Fadmin/realms/pharma-training/users",
        "/resources/../admin/realms/pharma-training/users",
        "/resources/..%2F..%2Fadmin/realms/pharma-training/users",

        // The master realm, whose token endpoint takes the administrator's password.
        "/REALMS/master/protocol/openid-connect/token",
        "/realms/master/protocol/openid-connect/token",
        "/realms/pharma-training/..%2Fmaster/protocol/openid-connect/token",
    ];

    // Staging forwards /admin/realms/pharma-training/users/** (#457, docs/adr/0042). Each of these starts inside that route
    // and climbs out of it, once Keycloak decodes or resolves the path, towards an admin endpoint the master administrator's
    // token reads with 200: the realm's clients or roles, the realm itself, or the master realm (#469). Plain, encoded in
    // either case, double-encoded, with ";" or a backslash.
    private static readonly string[] UsersRouteEscapes =
    [
        "/admin/realms/pharma-training/users/../clients",
        "/admin/realms/pharma-training/users/./../clients",
        "/admin/realms/pharma-training/users/..%2Fclients",
        "/admin/realms/pharma-training/users/..%2fclients",
        "/admin/realms/pharma-training/users/%2e%2e/clients",
        "/admin/realms/pharma-training/users/%2E%2E/roles",
        "/admin/realms/pharma-training/users/%2e%2e%2fclients",
        "/admin/realms/pharma-training/users/..;/clients",
        "/admin/realms/pharma-training/users/..%3B/clients",
        "/admin/realms/pharma-training/users/..%5Cclients",
        "/admin/realms/pharma-training/users/..\\clients",
        "/admin/realms/pharma-training/users/%252e%252e%252fclients",
        "/admin/realms/pharma-training/users/..%252Fclients",
        "/admin/realms/pharma-training/users/%2e%2e%2f",
        "/admin/realms/pharma-training/users/..%2F..%2Fmaster/users",
        "/admin/realms/pharma-training/users/%2e%2e/%2e%2e/master/users",
        "/admin/realms/pharma-training/users/%2e%2e%2f%2e%2e%2fmaster",
        "/admin/realms/pharma-training/users/..%2F..%2F..%2F..%2Frealms/master/.well-known/openid-configuration",

        // After a user's id, which a route takes.
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/..%2F..%2Fclients",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b%2F..%2F..%2Fclients",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/role-mappings/..%2F..%2F..%2Fclients",
    ];

    // The paths the demo seeder and X1 send under the users route (src/DemoSeeder/KeycloakUsers.cs, web/app/e2e/x1Users.ts),
    // with an id Keycloak does not hold: the proxy must forward each, whatever Keycloak answers.
    private static readonly string[] UsersRoutePaths =
    [
        "/admin/realms/pharma-training/users",
        "/admin/realms/pharma-training/users?username=nobody&exact=true",
        "/admin/realms/pharma-training/users/count",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/reset-password",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/role-mappings/realm",
        "/admin/realms/pharma-training/users/0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/role-mappings/realm/available",
    ];

    // Where the escapes aim: each answers 200 to the administrator's token straight from Keycloak.
    private static readonly string[] EscapeTargets =
    [
        "/admin/realms/pharma-training/clients",
        "/admin/realms/pharma-training/roles",
        "/admin/realms/pharma-training",
        "/admin/realms/master/users",
        "/admin/realms/master",
        "/realms/master/.well-known/openid-configuration",
    ];

    private readonly KeycloakAdmin keycloak = new(aspire);

    [Fact]
    public async Task The_proxy_forwards_the_realms_discovery_document()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var proxy = await StartProxyAsync(PublishedAppHost.Production, cancellationToken);
        using var client = Client();

        using var response = await client.GetAsync(Raw(proxy, $"/realms/{KeycloakAdmin.Realm}/.well-known/openid-configuration"), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "The proxy does not forward the realm's discovery document; the refused variants prove nothing.");
    }

    /// <summary>
    /// Staging forwards the realm's admin users (#457): there the administrator's token reaches the admin API through the
    /// proxy and answers 200, so a variant that reached it in production would answer 200 too, not 404.
    /// </summary>
    [Fact]
    public async Task An_admin_path_the_proxy_forwards_answers_200_with_the_administrators_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var proxy = await StartProxyAsync(PublishedAppHost.Staging, cancellationToken);
        using var identity = keycloak.Identity();
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.AdminTokenAsync(identity, cancellationToken));

        using var response = await client.GetAsync(Raw(proxy, $"/admin/realms/{KeycloakAdmin.Realm}/users/count"), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "Through the proxy, the administrator's token does not reach a forwarded admin path; a 404 for the variants proves nothing.");
    }

    [Fact]
    public async Task Percent_encoded_dot_segment_and_case_variants_of_admin_and_master_paths_are_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var proxy = await StartProxyAsync(PublishedAppHost.Production, cancellationToken);
        using var identity = keycloak.Identity();
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.AdminTokenAsync(identity, cancellationToken));

        List<string> answers = [];
        List<string> reached = [];
        foreach (var variant in Variants)
        {
            using var response = await client.GetAsync(Raw(proxy, variant), cancellationToken);
            var answer = $"{variant} answered {(int)response.StatusCode} {response.StatusCode}";
            answers.Add(answer);
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.BadRequest))
            {
                reached.Add(answer);
            }
        }

        reached.ShouldBeEmpty($"The auth proxy should refuse every variant with 404 or 400. Every answer: {string.Join("; ", answers)}");
    }

    /// <summary>
    /// #469: the escapes aim at endpoints the administrator's token reads, so one that reached its target would answer 200.
    /// Straight from Keycloak they do; through staging's proxy every escape must be refused with a 4xx by the proxy itself,
    /// never forwarded. Keycloak reads an encoded slash or a backslash as part of one user's id today, but the proxy does not
    /// lean on how a later Keycloak parses its paths.
    /// </summary>
    [Fact]
    public async Task Escapes_from_stagings_users_route_never_reach_another_admin_endpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ConcurrentQueue<string> forwarded = [];
        await using var proxy = await StartProxyAsync(PublishedAppHost.Staging, cancellationToken, forwarded);
        using var identity = keycloak.Identity();
        var token = new AuthenticationHeaderValue("Bearer", await keycloak.AdminTokenAsync(identity, cancellationToken));
        identity.DefaultRequestHeaders.Authorization = token;
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = token;

        foreach (var target in EscapeTargets)
        {
            using var direct = await identity.GetAsync(new Uri(target, UriKind.Relative), cancellationToken);
            direct.StatusCode.ShouldBe(HttpStatusCode.OK, $"Straight from Keycloak, {target} does not answer 200 to the administrator; an escape aimed at it proves nothing.");
        }

        List<string> answers = [];
        List<string> notRefused = [];
        foreach (var escape in UsersRouteEscapes)
        {
            forwarded.Clear();
            using var response = await client.GetAsync(Raw(proxy, escape), cancellationToken);
            var answer = $"{escape} answered {(int)response.StatusCode} {response.StatusCode}{(forwarded.IsEmpty ? string.Empty : " from Keycloak")}";
            answers.Add(answer);
            if ((int)response.StatusCode is < 400 or >= 500 || !forwarded.IsEmpty)
            {
                notRefused.Add(answer);
            }
        }

        notRefused.ShouldBeEmpty($"The proxy should refuse every escape from staging's users route itself, with a 4xx, and forward none. Every answer: {string.Join("; ", answers)}");
    }

    /// <summary>#469: confining the users route keeps every path the demo seeder and X1 send under it.</summary>
    [Fact]
    public async Task Stagings_users_route_forwards_every_path_the_seeder_and_x1_send()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ConcurrentQueue<string> forwarded = [];
        await using var proxy = await StartProxyAsync(PublishedAppHost.Staging, cancellationToken, forwarded);
        using var client = Client();

        List<string> refused = [];
        foreach (var path in UsersRoutePaths)
        {
            forwarded.Clear();
            using var response = await client.GetAsync(Raw(proxy, path), cancellationToken);
            if (forwarded.IsEmpty)
            {
                refused.Add($"{path} answered {(int)response.StatusCode} {response.StatusCode} from the proxy");
            }
        }

        refused.ShouldBeEmpty("Staging's proxy should forward every path the demo seeder and X1 send to Keycloak.");
    }

    /// <summary>
    /// The auth proxy as the environment publishes it, its cluster pointed at run mode's Keycloak; dispose it. Each request
    /// it forwards goes into <paramref name="forwarded"/>.
    /// </summary>
    private async Task<WebApplication> StartProxyAsync(string environment, CancellationToken cancellationToken, ConcurrentQueue<string>? forwarded = null)
    {
        var keycloakAddress = $"{aspire.App.GetEndpoint("keycloak", "http")}".TrimEnd('/');
        Dictionary<string, string?> settings;
        await using (var model = await PublishedAppHost.ModelAsync(environment, cancellationToken))
        {
            var auth = model.Resources.SingleOrDefault(resource => resource.Name == Auth);
            auth.ShouldNotBeNull("Published, there is no auth proxy.");
            var context = new EnvironmentCallbackContext(model.ExecutionContext, auth, cancellationToken: cancellationToken);
            foreach (var callback in auth.Annotations.OfType<EnvironmentCallbackAnnotation>())
            {
                await callback.Callback(context);
            }

            settings = context.EnvironmentVariables
                .Where(variable => variable.Key.StartsWith(ReverseProxy, StringComparison.Ordinal))
                .ToDictionary(
                    variable => variable.Key.Replace("__", ":", StringComparison.Ordinal),
                    variable => (string?)Setting(variable.Key, variable.Value, keycloakAddress),
                    StringComparer.OrdinalIgnoreCase);
        }

        settings.ShouldNotBeEmpty("The published auth proxy configures no routes.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        var proxy = builder.Build();

        // Every request one of the proxy's routes matched, so it goes on to Keycloak: the only endpoints here are YARP's.
        proxy.Use((context, next) =>
        {
            if (context.GetEndpoint() is not null)
            {
                forwarded?.Enqueue($"{context.Request.Path}");
            }

            return next(context);
        });
        proxy.MapReverseProxy();
        await proxy.StartAsync(cancellationToken);
        return proxy;
    }

    /// <summary>A setting of the published proxy as it reads it; a destination's address becomes run mode's Keycloak.</summary>
    private static string Setting(string name, object value, string keycloakAddress)
    {
        if (name.EndsWith("__ADDRESS", StringComparison.Ordinal))
        {
            return keycloakAddress;
        }

        return value.ShouldBeOfType<string>($"The auth proxy's setting {name} is not a literal; this test cannot mirror it.");
    }

    /// <summary>The proxy's address and the path exactly as written: no dot segment removed, no escape decoded.</summary>
    private static Uri Raw(WebApplication proxy, string path) =>
        new($"{proxy.Urls.First().TrimEnd('/')}{path}", new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

    private static HttpClient Client() => new(new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true);
}
