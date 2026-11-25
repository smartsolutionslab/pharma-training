using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: AssemblyFixture(typeof(SmartSolutionsLab.PharmaTraining.Integration.Tests.AspireFixture))]

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>The whole application, started once per test run on empty containers.</summary>
public sealed partial class AspireFixture : IAsyncLifetime
{
    /// <summary>Every editorial model limit of this run, per minute and per day, for content review and test draft alike.</summary>
    public const int EditorialModelLimit = 50;

    /// <summary>The per-address limit on public certificate verification of this run, per minute (docs/adr/0029).</summary>
    public const int CertificationVerificationLimit = 20;

    /// <summary>The per-address limit on public certificate verification of this run, per day.</summary>
    public const int CertificationVerificationDailyLimit = 1000;

    /// <summary>How often Certification purges erased persons' certificates in this run (#115, slice 5), down from a day.</summary>
    public static readonly TimeSpan ErasedCertificatePurgeInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many invitations one user may accept a minute in this run (docs/adr/0004), raised from the default 10.</summary>
    public const int OrganisationAcceptsPerMinute = 30;

    /// <summary>How many invitation mails one user may send a UTC day in this run (#265), raised from the default 100.</summary>
    public const int InvitationMailsPerSenderPerDay = 1000;

    /// <summary>How often Learning's and Assessment's announcement sweeps run in this run (#277), lowered from the default minute.</summary>
    public static readonly TimeSpan AnnouncementSweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long the sweeps leave a stored event to its handler in this run (#277), lowered from the default two minutes.</summary>
    public static readonly TimeSpan AnnouncementGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>How often Compliance's, Learning's and Certification's sweeps of erased persons' messages run in this run (#439), lowered from the default minute.</summary>
    public static readonly TimeSpan ErasedPersonMessageSweepInterval = TimeSpan.FromSeconds(1);

    // Twice the largest start-up of the green CI runs listed in #134 (204 s), rounded up to the minute. Before #126 the
    // edge and every service host were rebuilt at each start, which took 3.5 to over 5 minutes; now every project starts
    // from the AppHost's build. The fixture logs each run's start-up beside this limit.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(7);

    private DistributedApplication? app;

    private ILogger? logger;

    private TimeSpan startedIn;

    public DistributedApplication App => app ?? throw new InvalidOperationException("The application has not started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var started = TimeProvider.System.GetTimestamp();
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);

        // Tests start from empty containers: never a developer's data, never a password a volume remembers.
        foreach (var resource in builder.Resources)
        {
            foreach (var volume in resource.Annotations.OfType<ContainerMountAnnotation>().Where(mount => mount.Type == ContainerMountType.Volume).ToList())
            {
                resource.Annotations.Remove(volume);
            }
        }

        // The integration tests run the application as a developer without a key does, with no model configured: the run
        // shows the application starting that way, and never uses a developer's own endpoint or key from user secrets.
        builder.Configuration["Parameters:azure-openai-endpoint"] = string.Empty;
        builder.Configuration["Parameters:azure-openai-api-key"] = string.Empty;

        // The run sets every editorial model limit (docs/adr/0020) to EditorialModelLimit, a minute and a day alike, so
        // EditorialModelLimitsTests reaches it with editor-b, a user only they call the model as. Every other test shares
        // the editor's budget, which this leaves far above what they use (a handful of calls per endpoint in the whole run),
        // and above the default 10 a minute, so no test is refused for another's calls.
        var editorial = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "editorial"));
        foreach (var setting in new[] { "ContentReview__PerMinute", "ContentReview__PerDay", "TestDraft__PerMinute", "TestDraft__PerDay" })
        {
            editorial.WithEnvironment($"Editorial__ModelLimits__{setting}", EditorialModelLimit.ToString(CultureInfo.InvariantCulture));
        }

        // The run uses four eyes (docs/adr/0007, #246), so its refusals and another editor's publish are shown end to end
        // without a second application run: every test publishes as editor-c (EditorialWorkflow.Publisher), who neither
        // submits nor approves. The default, four eyes off, is the unit tests' of the domain, the handler and EditorialSettings.
        editorial.WithEnvironment("Editorial__FourEyes", "true");

        // The run sets the per-address verification limit (docs/adr/0029) to CertificationVerificationLimit a minute, so
        // CertificationVerificationTests reaches it in a few dozen calls, and the day far above it, so the minute alone
        // refuses. Every local caller reaches Certification from one address, the edge's, so every call of the run shares
        // one count: only that collection calls /certification/verify, and it leaves the minute's window reset.
        var certification = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "certification"));
        certification.WithEnvironment("Certification__VerificationLimits__PerMinute", CertificationVerificationLimit.ToString(CultureInfo.InvariantCulture));
        certification.WithEnvironment("Certification__VerificationLimits__PerDay", CertificationVerificationDailyLimit.ToString(CultureInfo.InvariantCulture));

        // Certification purges an erased person's retained certificates once no pharmacy keeps them (#115, slice 5), once a
        // day by default; the run purges every ErasedCertificatePurgeInterval, so PersonErasureTests sees a shortened
        // period take effect within seconds.
        certification.WithEnvironment("Certification__ErasedCertificatePurge__Interval", ErasedCertificatePurgeInterval.ToString("c", CultureInfo.InvariantCulture));

        // The accept tests accept as employee-a up to about 15 times in quick succession, more than the default 10 a minute
        // (docs/adr/0004), so the run raises the per-minute accept limit to OrganisationAcceptsPerMinute; every user stays
        // under it, and only OrganisationAcceptLimitTests accepts as visitor, to reach it.
        var organisation = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "organisation"));
        organisation.WithEnvironment("Organisation__AcceptLimit__PerMinute", OrganisationAcceptsPerMinute.ToString(CultureInfo.InvariantCulture));

        // Every invite and renewal as owner counts on owner's one sender tally (#265), about 50 a run and growing, more
        // than the default 100 a day would leave room for. The run raises the cap to InvitationMailsPerSenderPerDay, and
        // OrganisationMailRecipientAndSenderLimitsTests reaches it with editor-b, a user only it invites as.
        organisation.WithEnvironment("Organisation__InvitationLimits__MailsPerSenderPerDay", InvitationMailsPerSenderPerDay.ToString(CultureInfo.InvariantCulture));

        // The announcement sweeps (#277) announce a stored ContentCompleted or ExaminationPassed whose announcement never
        // left once it is older than the grace period, every interval. The defaults (a minute, two minutes) would keep
        // LearningAnnouncementSweepTests and AssessmentAnnouncementSweepTests waiting for minutes, so the run sweeps every
        // AnnouncementSweepInterval after AnnouncementGracePeriod. Five seconds is still far above the milliseconds a
        // report or submit takes between its append and its outbox commit, even under CI load, so no other test's
        // announcement is ever taken for a lost one.
        foreach (var (service, module) in new[] { ("learning", "Learning"), ("assessment", "Assessment") })
        {
            var sweeping = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == service));
            sweeping.WithEnvironment($"{module}__AnnouncementSweep__Interval", AnnouncementSweepInterval.ToString("c", CultureInfo.InvariantCulture));
            sweeping.WithEnvironment($"{module}__AnnouncementSweep__GracePeriod", AnnouncementGracePeriod.ToString("c", CultureInfo.InvariantCulture));
        }

        // The sweeps of erased persons' messages (#439) remove the dead letters and handled envelopes of a tombstoned
        // person's membership messages every interval. The default minute would keep ErasedPersonMessageSweepTests
        // waiting, so the run sweeps every ErasedPersonMessageSweepInterval. A sweep only ever removes rows of tombstoned
        // persons, and every test that plants a tombstone uses a fresh person, so no other test's row is ever swept.
        foreach (var (service, module) in new[] { ("compliance", "Compliance"), ("learning", "Learning"), ("certification", "Certification") })
        {
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == service))
                .WithEnvironment($"{module}__ErasedPersonMessageSweep__Interval", ErasedPersonMessageSweepInterval.ToString("c", CultureInfo.InvariantCulture));
        }

        app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("host", timeout.Token);

        // In the run's log, so a slow start shows how close it came to the limit before it ever reaches it.
        startedIn = TimeProvider.System.GetElapsedTime(started);
        logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AspireFixture>();
        LogStarted(logger, startedIn, StartupTimeout);
    }

    public HttpClient CreateHostClient() => App.CreateHttpClient("host");

    /// <summary>A client calling the host as a realm user; local users' passwords equal their names.</summary>
    public async Task<HttpClient> CreateHostClientAsync(string user)
    {
        var accessToken = await RequestAccessTokenAsync(user);

        var client = CreateHostClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>The access token the realm issues to a local user, whose password equals their name.</summary>
    public async Task<string> RequestAccessTokenAsync(string user)
    {
        using var identity = new HttpClient { BaseAddress = App.GetEndpoint("keycloak", "http") };
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "pharma-training-web",
            ["username"] = user,
            ["password"] = user,
        });
        using var response = await identity.PostAsync(new Uri("/realms/pharma-training/protocol/openid-connect/token", UriKind.Relative), form);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("The realm returned no token.");
        return token.AccessToken;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            // Again at the end, where a CI log's tail (all a job-log viewer may show) still has it.
            if (logger is not null)
            {
                LogStarted(logger, startedIn, StartupTimeout);
            }

            await app.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The application started in {Elapsed} (limit {StartupTimeout})")]
    private static partial void LogStarted(ILogger logger, TimeSpan elapsed, TimeSpan startupTimeout);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
