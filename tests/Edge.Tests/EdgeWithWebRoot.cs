using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// The edge (src/Edge) in this process, as the AppHost configures it, with the web root, environment and module service of
/// the test's choosing: <c>Authentication:Authority</c> is <see cref="EdgeHost.Authority"/>, <c>Edge:Modules</c> names
/// <c>catalog</c> and <c>editorial</c>, and service discovery finds both at <paramref name="moduleService"/>; any further
/// <paramref name="settings"/> are set as the AppHost would set them (<c>Edge:BlobEndpoint</c>, published).
/// </summary>
public sealed class EdgeWithWebRoot(string environment, string webRoot, Uri moduleService, IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public static readonly string[] Modules = ["catalog", "editorial"];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseWebRoot(webRoot);
        builder.UseSetting("Authentication:Authority", EdgeHost.Authority);
        foreach (var module in Modules)
        {
            builder.UseSetting($"Edge:Modules:{module}", module);
            builder.UseSetting($"services:{module}:http:0", moduleService.ToString().TrimEnd('/'));
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, TestTokens.Trust));
    }
}
