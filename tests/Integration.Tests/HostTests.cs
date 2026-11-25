using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

public class HostTests(AspireFixture aspire)
{
    private static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] UpdatePassword = ["UPDATE_PASSWORD"];

    [Fact]
    public async Task Every_health_checked_resource_becomes_healthy()
    {
        // Aspire adds helper resources of its own (a project rebuilder, for one) that never report health.
        var resources = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .Where(resource => resource.Annotations.OfType<HealthCheckAnnotation>().Any())
            .Select(resource => resource.Name)
            .ToList();

        List<string> unhealthy = [];
        foreach (var resource in resources)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(ResourceTimeout);
            try
            {
                await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(resource, timeout.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                unhealthy.Add(resource);
            }
        }

        string.Join(", ", unhealthy).ShouldBe(string.Empty);
    }

    [Fact]
    public async Task The_host_reports_itself_healthy()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_api_is_described_by_an_openapi_document()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_anonymous_caller_is_rejected()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(new Uri("/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_the_identity_provider_identifies_the_user()
    {
        using var client = await aspire.CreateHostClientAsync("developer");

        var me = await client.GetFromJsonAsync<Me>(new Uri("/me", UriKind.Relative), TestContext.Current.CancellationToken);

        me?.Name.ShouldBe("developer");
    }

    [Fact]
    public async Task A_reset_password_email_from_the_realm_reaches_mailpit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = await new KeycloakAdmin(aspire).AdministratorAsync(cancellationToken);
        var users = await identity.GetFromJsonAsync<RealmUser[]>(new Uri("/admin/realms/pharma-training/users?username=developer&exact=true", UriKind.Relative), cancellationToken) ?? [];

        using var sent = await identity.PutAsJsonAsync(new Uri($"/admin/realms/pharma-training/users/{users.Single().Id}/execute-actions-email", UriKind.Relative), UpdatePassword, cancellationToken);

        sent.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var recipient = await RecipientAsync("developer@example.com", cancellationToken);
        recipient.ShouldBe("developer@example.com");
    }

    /// <summary>The recipient of the first email Mailpit caught for the address, or an empty string when none arrives.</summary>
    private async Task<string> RecipientAsync(string address, CancellationToken cancellationToken)
    {
        using var mailpit = new HttpClient { BaseAddress = aspire.App.GetEndpoint("mailpit", "http") };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var inbox = await mailpit.GetFromJsonAsync<Inbox>(new Uri($"/api/v1/search?query=to:{address}", UriKind.Relative), cancellationToken);
            var recipient = inbox?.Messages.SelectMany(message => message.To).FirstOrDefault(to => to.Address == address);
            if (recipient is not null)
            {
                return recipient.Address;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return string.Empty;
    }

    private sealed record Me(string? Name);

    private sealed record Tenant(string? TenantId);

    private sealed record RealmUser(string Id);

    private sealed record Inbox(IReadOnlyList<Message> Messages);

    private sealed record Message(IReadOnlyList<Recipient> To);

    private sealed record Recipient(string Address);
}
