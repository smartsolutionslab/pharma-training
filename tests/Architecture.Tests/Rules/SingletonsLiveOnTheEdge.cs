using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// What the application has once lives on the edge, src/Edge (ADR-0017): /me, a SignalR hub or event stream and
/// POST /agents/{name}, which the edge forwards to the service that owns the agent. A service host that maps one of
/// them answers it beside the edge, once per service. A service's MCP server and AG-UI endpoint are its own, at
/// /&lt;module&gt;/mcp and /&lt;module&gt;/agents/{name}, never at its root, where they would claim to be the
/// application's. It
/// reads every service host's own endpoint table once in Development and once in Production, so an endpoint mapped in
/// only one of them is caught too.
/// </summary>
public sealed class SingletonsLiveOnTheEdge : IArchitectureRule
{
    private const string HubMetadata = "Microsoft.AspNetCore.SignalR.HubMetadata";

    /// <summary>The route prefixes of what exists once, or must not look so: /me, the event stream, /mcp and /agents at the root.</summary>
    private static readonly string[] Singletons = ["/me", "/hubs", "/server-events", "/mcp", "/agents"];

    public string Description => "No service host maps /me, a hub, /mcp or /agents at its root: what the application has once lives on the edge.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. ServiceHostEntryPoints.All
            .SelectMany(entryPoint => Mapped(entryPoint, Environments.Development).Concat(Mapped(entryPoint, Environments.Production)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> Mapped(Type entryPoint, string environment)
    {
        using var host = (IMappedHost)Activator.CreateInstance(typeof(MappedHost<>).MakeGenericType(entryPoint), environment)!;
        var service = "src/Services/" + entryPoint.Assembly.GetName().Name!.Split('.')[^2] + "/Host";
        return [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsSingleton)
            .Select(endpoint => $"{service} maps {Name(endpoint)}, which the application has once: map it on the edge (src/Edge) through edge.endpoints.")];
    }

    private static bool IsSingleton(RouteEndpoint endpoint)
    {
        var route = "/" + endpoint.RoutePattern.RawText?.TrimStart('/');
        return Singletons.Any(prefix => route == prefix || route.StartsWith(prefix + "/", StringComparison.Ordinal))
            || endpoint.Metadata.Any(metadata => metadata.GetType().FullName == HubMetadata);
    }

    private static string Name(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
        var route = "/" + endpoint.RoutePattern.RawText?.TrimStart('/');
        return methods.Count == 0 ? route : $"{string.Join(',', methods)} {route}";
    }

    private interface IMappedHost : IDisposable
    {
        IServiceProvider Services { get; }
    }

    /// <summary>
    /// A service host with every endpoint mapped but nothing started: no hosted service runs, so the configuration
    /// Aspire would provide only has to be present, never reachable.
    /// </summary>
    private sealed class MappedHost<TEntryPoint>(string environment) : WebApplicationFactory<TEntryPoint>, IMappedHost
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:database", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:messaging", "amqp://localhost");
            builder.UseSetting("Messaging:Queue", "unused");
            builder.UseSetting("Authentication:Authority", "http://localhost/realms/unused");
            builder.UseSetting("ConnectionStrings:mail", "smtp://localhost");
            builder.UseSetting("Mail:From", "noreply@example.com");
            builder.UseSetting("Frontend:BaseUrl", "https://app.example");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
