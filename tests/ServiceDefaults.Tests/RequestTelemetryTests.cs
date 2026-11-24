using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Tests;

/// <summary>
/// What a host's telemetry records of a request's path (#311). A route marked <see cref="RedactedRoute"/>, as
/// Certification's public verification is, carries a credential in its path: its span records the route template as
/// <c>url.path</c>, never the path it was called with. Every log written during a request carries the route template as
/// its <c>RequestPath</c> scope, in place of the path ASP.NET Core's hosting would put there, so the code never leaves
/// the process. A real server on a loopback port, with the service defaults every host adds.
/// </summary>
public partial class RequestTelemetryTests
{
    private const string Verify = "/certification/verify/{code}";

    [Fact]
    public async Task A_redacted_routes_span_records_its_template_never_the_code()
    {
        var code = Guid.CreateVersion7().ToString("N");
        using var activities = new RecordedActivities();

        await RequestAsync($"/certification/verify/{code}");

        var server = activities.Stopped.Where(activity => (activity.GetTagItem("url.path") as string)?.StartsWith("/certification/verify", StringComparison.Ordinal) == true).ToList();
        server.ShouldNotBeEmpty("No server span for the verification was recorded.");
        server.ShouldAllBe(activity => (string?)activity.GetTagItem("url.path") == Verify);
        activities.Stopped.SelectMany(activity => activity.TagObjects).Where(tag => tag.Value != null && tag.Value.ToString()!.Contains(code, StringComparison.Ordinal))
            .ShouldBeEmpty("A span carries the verification code.");
        activities.Stopped.Where(activity => activity.DisplayName.Contains(code, StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_route_not_marked_keeps_its_path_in_its_span()
    {
        var id = Guid.CreateVersion7().ToString("N");
        using var activities = new RecordedActivities();

        await RequestAsync($"/catalog/courses/{id}");

        activities.Stopped.ShouldContain(activity => (string?)activity.GetTagItem("url.path") == $"/catalog/courses/{id}");
    }

    [Fact]
    public async Task A_log_written_during_a_request_carries_the_route_template_as_its_request_path()
    {
        var code = Guid.CreateVersion7().ToString("N");
        var logs = new CapturingLoggerProvider();

        await RequestAsync($"/certification/verify/{code}", logs);

        var verifying = logs.Logs.Where(log => log.Message == "Verifying").ShouldHaveSingleItem();
        verifying.Scopes.ShouldContain($"RequestPath={Verify}");
        logs.Logs.SelectMany(log => log.Scopes.Append(log.Message)).Where(value => value.Contains(code, StringComparison.Ordinal))
            .ShouldBeEmpty("A log or its scopes carries the verification code.");
    }

    [Fact]
    public async Task A_log_of_a_route_not_marked_carries_its_template_too()
    {
        var id = Guid.CreateVersion7().ToString("N");
        var logs = new CapturingLoggerProvider();

        await RequestAsync($"/catalog/courses/{id}", logs);

        logs.Logs.Where(log => log.Message == "Reading").ShouldHaveSingleItem().Scopes.ShouldContain("RequestPath=/catalog/courses/{id}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Verifying")]
    private static partial void LogVerifying(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading")]
    private static partial void LogReading(ILogger logger);

    /// <summary>Serves the request with a host as every service builds it: the service defaults, then its endpoints.</summary>
    private static async Task RequestAsync(string path, CapturingLoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.AddServiceDefaults();
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        await using var app = builder.Build();
        app.MapGet(Verify, (string code, ILogger<RequestTelemetryTests> logger) =>
        {
            LogVerifying(logger);
            return "verified";
        }).WithMetadata(new RedactedRoute());
        app.MapGet("/catalog/courses/{id}", (string id, ILogger<RequestTelemetryTests> logger) =>
        {
            LogReading(logger);
            return "course";
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
