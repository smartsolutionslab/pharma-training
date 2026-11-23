using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// An endpoint is anonymous only where it is mapped, with <c>.AllowAnonymous()</c>; everything else requires
/// authorization. Module routes inherit it from their group, so this catches what is mapped beside a group or on the
/// host. It reads the host's own endpoint table once in Development and once in Production, so an endpoint mapped in
/// only one of them is checked too.
/// </summary>
public sealed class EveryEndpointDeclaresItsAuthorization : IArchitectureRule
{
    public string Description => "Every endpoint of the host requires authorization or is mapped with AllowAnonymous().";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. Undeclared(Environments.Development)
            .Concat(Undeclared(Environments.Production))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> Undeclared(string environment) =>
        [.. ServiceHostEntryPoints.All.SelectMany(entryPoint => Undeclared(entryPoint, environment))];

    private static List<string> Undeclared(Type entryPoint, string environment)
    {
        using var host = (IMappedHost)Activator.CreateInstance(typeof(MappedHost<>).MakeGenericType(entryPoint), environment)!;
        return [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(endpoint => $"{Name(endpoint)} is mapped with neither RequireAuthorization() nor AllowAnonymous().")];
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
    /// The host with every endpoint mapped but nothing started: no hosted service runs, so the configuration Aspire
    /// would provide only has to be present, never reachable.
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
            builder.UseSetting("Messaging:Publishers:unused", "unused");
            builder.UseSetting("Authentication:Authority", "http://localhost/realms/unused");
            builder.UseSetting("ConnectionStrings:mail", "smtp://localhost");
            builder.UseSetting("Mail:From", "noreply@example.com");
            builder.UseSetting("Frontend:BaseUrl", "https://app.example");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
