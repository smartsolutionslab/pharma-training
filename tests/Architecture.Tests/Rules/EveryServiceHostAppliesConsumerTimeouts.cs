using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Wolverine;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// A consumer that needs longer than Wolverine's 60-second default says so with <c>[ConsumerTimeout]</c>
/// (<c>src/BuildingBlocks/Messaging</c>), which only <see cref="ConsumerTimeoutPolicy"/> turns into its chain's timeout.
/// A service host whose messaging lost the policy would cancel such a consumer after 60 seconds, so every host's Wolverine
/// options are read, without starting it, and must hold the policy.
/// </summary>
public sealed class EveryServiceHostAppliesConsumerTimeouts : IArchitectureRule
{
    public string Description => "Every service host applies [ConsumerTimeout] through ConsumerTimeoutPolicy.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return [.. ServiceHostEntryPoints.All
            .Where(entryPoint => !AppliesConsumerTimeouts(entryPoint))
            .Select(entryPoint => "src/Services/" + entryPoint.Assembly.GetName().Name!.Split('.')[^2] + "/Host does not apply [ConsumerTimeout]: its Wolverine options hold no ConsumerTimeoutPolicy (UseConsumerTimeouts()).")];
    }

    private static bool AppliesConsumerTimeouts(Type entryPoint)
    {
        using var host = (IConfiguredHost)Activator.CreateInstance(typeof(ConfiguredHost<>).MakeGenericType(entryPoint))!;
        return host.Services.GetRequiredService<WolverineOptions>().Policies.OfType<ConsumerTimeoutPolicy>().Any();
    }

    private interface IConfiguredHost : IDisposable
    {
        IServiceProvider Services { get; }
    }

    /// <summary>The host built but nothing started: no hosted service runs, so the configuration Aspire would provide only has to be present.</summary>
    private sealed class ConfiguredHost<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IConfiguredHost
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
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
