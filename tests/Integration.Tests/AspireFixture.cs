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
    // Twice the slowest start-up measured on the CI runner since every project starts from the AppHost's build
    // (#126: 136, 182 and 204 s), rounded up to the minute; the fixture logs each run's start-up beside this limit.
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
