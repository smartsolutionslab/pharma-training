using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// The Organisation service's own Wolverine failure policy, read from the host as it configures it (src/Host/Wiring,
/// <c>AddMessaging</c>) and asked what it does on the first failure. At the cap of an invitation's links a retry cannot
/// succeed until a manager renews the invitation, so <see cref="InvitationLinkLimitReachedException"/> goes to the dead
/// letters at once instead of walking the retry ladder (1 … 32 s; #193's review, follow-up 2). Any other failure is
/// still retried. Wolverine asks its rules in the order they were added and takes the first that matches, as
/// <see cref="FirstContinuation"/> does.
/// </summary>
public class InvitationLinkLimitDeadLettersAtOnceTests
{
    private const string MoveToErrorQueue = "Wolverine.ErrorHandling.MoveToErrorQueue";

    private const string ScheduledRetry = "Wolverine.ErrorHandling.ScheduledRetryContinuation";

    [Fact]
    public void The_organisation_service_dead_letters_a_reached_link_limit_on_its_first_failure_without_a_retry()
    {
        using var host = new ConfiguredHost();

        var continuation = FirstContinuation(host.Failures, new InvitationLinkLimitReachedException(Guid.CreateVersion7()), attempt: 1);

        continuation.ShouldBe(MoveToErrorQueue, $"the policy: {Describe(host.Failures)}");
    }

    [Fact]
    public void The_organisation_service_still_retries_any_other_failure_on_its_first_attempt()
    {
        using var host = new ConfiguredHost();

        var continuation = FirstContinuation(host.Failures, new InvalidOperationException("The database did not answer."), attempt: 1);

        continuation.ShouldBe(ScheduledRetry, $"the policy: {Describe(host.Failures)}");
    }

    private static string FirstContinuation(FailureRuleCollection failures, Exception failure, int attempt)
    {
        var envelope = new Envelope { Attempts = attempt };
        foreach (var rule in failures)
        {
            if (rule.TryCreateContinuation(failure, envelope, out var continuation))
            {
                return continuation.GetType().FullName!;
            }
        }

        return "none: Wolverine's default";
    }

    private static string Describe(FailureRuleCollection failures) => string.Join(" | ", failures.Select(rule => rule.ToString()));

    /// <summary>
    /// The Organisation service host with its services built but nothing started: no hosted service runs, so the
    /// configuration Aspire would provide only has to be present, never reachable.
    /// </summary>
    private sealed class ConfiguredHost : IDisposable
    {
        private readonly IDisposable factory;

        public ConfiguredHost()
        {
            var entryPoint = ServiceHostEntryPoints.All.Single(type => type.Assembly.GetName().Name == "SmartSolutionsLab.PharmaTraining.Services.Organisation.Host");
            var host = (IServiceProviderSource)Activator.CreateInstance(typeof(Factory<>).MakeGenericType(entryPoint))!;
            factory = host;
            Failures = ((IWithFailurePolicies)host.Services.GetRequiredService<WolverineOptions>()).Failures;
        }

        public FailureRuleCollection Failures { get; }

        public void Dispose() => factory.Dispose();
    }

    private interface IServiceProviderSource : IDisposable
    {
        IServiceProvider Services { get; }
    }

    private sealed class Factory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IServiceProviderSource
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
