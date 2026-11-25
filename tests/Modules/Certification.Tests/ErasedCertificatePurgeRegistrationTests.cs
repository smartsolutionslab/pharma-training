using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.PurgeErasedCertificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests;

/// <summary>
/// The module runs its purge of erased persons' certificates (#115, slice 5) as a hosted service that sends
/// <see cref="PurgeErasedCertificates"/> every interval, and reads the interval when its services are added, so a value
/// the host cannot read stops it at startup.
/// </summary>
public class ErasedCertificatePurgeRegistrationTests
{
    [Fact]
    public void The_module_runs_the_purge_as_a_hosted_service()
    {
        var services = ModuleServices(Configuration());

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<PeriodicCommandService<PurgeErasedCertificates>>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_unreadable_interval_stops_the_module_when_its_services_are_added()
    {
        var configuration = Configuration((ErasedCertificatePurgeSettings.IntervalKey, "abc"));

        var refused = Should.Throw<InvalidOperationException>(() => ModuleServices(configuration));

        refused.Message.ShouldContain("Certification:ErasedCertificatePurge:Interval", Case.Sensitive);
    }

    private static ServiceCollection ModuleServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));

        // The host's messaging registers the purge the module's sweep of erased persons' messages uses (#439); the
        // module's container alone stands in for it.
        services.AddSingleton(Substitute.For<IPersonalMessagePurge>());
        new CertificationModule().AddServices(services, configuration);
        return services;
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" }
                    .Concat(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value))))
            .Build();
}
