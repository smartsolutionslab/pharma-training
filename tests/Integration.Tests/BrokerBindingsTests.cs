using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the running broker holds, asked of RabbitMQ's management API: an event that a module of a host both publishes
/// and consumes travels on the host's local queue only, so the broker has no exchange or binding for it, and nobody
/// with broker credentials can hand the host one; an event another service publishes is still bound to its consumer's queue.
/// </summary>
public class BrokerBindingsTests(AspireFixture aspire)
{
    private const string InvitationMailRequested = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.InvitationMailRequestedV1";

    // Organisation consumes no event another service publishes; Compliance's binding to Organisation's MembershipStartedV1
    // is the one a host needs.
    private const string MembershipStarted = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task The_broker_has_no_exchange_or_binding_for_the_invitation_mail_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var management = await ManagementClientAsync(cancellationToken);
        await WaitForBindingAsync(management, MembershipStarted, "compliance", cancellationToken);

        var bindings = await BindingsAsync(management, cancellationToken);
        var exchanges = await management.GetFromJsonAsync<List<Exchange>>(new Uri("/api/exchanges/%2F", UriKind.Relative), cancellationToken) ?? [];

        bindings.Where(binding => binding.Source == InvitationMailRequested)
            .Select(binding => $"{binding.Source} -> {binding.DestinationType} {binding.Destination}")
            .ShouldBeEmpty("the broker routes InvitationMailRequestedV1, which Organisation's host publishes and consumes locally, to a queue");
        exchanges.Select(exchange => exchange.Name).ShouldNotContain(InvitationMailRequested, "the broker holds an exchange for InvitationMailRequestedV1");
    }

    [Fact]
    public async Task An_event_another_service_publishes_is_still_bound_to_its_consumers_queue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var management = await ManagementClientAsync(cancellationToken);

        await WaitForBindingAsync(management, MembershipStarted, "compliance", cancellationToken);

        (await BindingsAsync(management, cancellationToken))
            .ShouldContain(binding => binding.Source == MembershipStarted && binding.Destination == "compliance" && binding.DestinationType == "queue");
    }

    /// <summary>The management API of the application's RabbitMQ, signed in with the broker's own user from its connection string.</summary>
    private async Task<HttpClient> ManagementClientAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("messaging", cancellationToken)
            ?? throw new InvalidOperationException("The broker has no connection string.");
        var userInfo = new Uri(connectionString).UserInfo.Split(':', 2);
        var credentials = $"{Uri.UnescapeDataString(userInfo[0])}:{Uri.UnescapeDataString(userInfo[1])}";
        var client = new HttpClient { BaseAddress = aspire.App.GetEndpoint("messaging", "management") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        return client;
    }

    private static async Task<List<Binding>> BindingsAsync(HttpClient management, CancellationToken cancellationToken) =>
        await management.GetFromJsonAsync<List<Binding>>(new Uri("/api/bindings/%2F", UriKind.Relative), cancellationToken) ?? [];

    /// <summary>Waits until the services' listeners have bound their queues, so an absence below is not mere earliness.</summary>
    private static async Task WaitForBindingAsync(HttpClient management, string exchange, string queue, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var bindings = await BindingsAsync(management, cancellationToken);
            if (bindings.Exists(binding => binding.Source == exchange && binding.Destination == queue))
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"The queue {queue} was not bound to {exchange} after {WaitTimeout.TotalSeconds:0} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private sealed record Binding(
        string Source,
        string Destination,
        [property: JsonPropertyName("destination_type")] string DestinationType);

    private sealed record Exchange(string Name);
}
