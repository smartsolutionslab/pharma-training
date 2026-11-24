using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// The address a service's per-client limit counts, behind the Container Apps ingress (#292, docs/adr/0031). The edge
/// sends the client's address as the one <c>X-Forwarded-For</c> entry; the internal ingress in front of the service may
/// append the edge's own. Published, the service trusts exactly one hop, as the edge does
/// (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>), which takes the ingress's entry off the list, so
/// <see cref="ClientRateSubject"/> counts the client either way. A real server on a loopback port, with the setting as
/// publishing gives it or without it, as in run mode.
/// </summary>
public class ServiceBehindTheIngressTests
{
    private const string Client = "203.0.113.9";

    [Fact]
    public async Task Published_the_client_is_counted_when_the_internal_ingress_appends_the_edges_address()
    {
        var subject = await SubjectAsync(published: true, $"{Client}, 10.0.4.17");

        subject.ShouldBe($"address:{Client}");
    }

    [Fact]
    public async Task Published_the_client_is_counted_when_the_internal_ingress_appends_nothing()
    {
        var subject = await SubjectAsync(published: true, Client);

        subject.ShouldBe($"address:{Client}");
    }

    [Fact]
    public async Task In_run_mode_the_edges_entry_is_counted_as_it_always_was()
    {
        var subject = await SubjectAsync(published: false, Client);

        subject.ShouldBe($"address:{Client}");
    }

    /// <summary>The subject a service counts for a request with this <c>X-Forwarded-For</c>, from a loopback connection.</summary>
    private static async Task<string> SubjectAsync(bool published, string forwardedFor)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (published)
        {
            builder.WebHost.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
        }

        await using var app = builder.Build();
        app.MapGet("/subject", (Microsoft.AspNetCore.Http.HttpContext context) => ClientRateSubject.Of(context));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/subject");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var subject = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);
        return subject;
    }
}
