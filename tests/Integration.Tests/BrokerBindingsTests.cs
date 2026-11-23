using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the running broker holds, asked of RabbitMQ's management API: an event that a module of a host both publishes
/// and consumes travels on the host's local queue only, so the broker has no exchange or binding for it, and nobody
/// with broker credentials can hand the host one; an event another service publishes is still bound to its consumer's queue.
/// </summary>
public partial class BrokerBindingsTests(AspireFixture aspire)
{
    private const string OrganisationQueue = "organisation";

    private const string InvitationMailRequested = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.InvitationMailRequestedV1";

    // Organisation consumes no event another service publishes; Compliance's binding to Organisation's MembershipStartedV1
    // is the one a host needs.
    private const string MembershipStarted = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // How long a forged mail request is given to be handled; the consumer handles a real one within a second or two.
    private static readonly TimeSpan ForgeryWait = TimeSpan.FromSeconds(15);

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire);

    [Fact]
    public async Task The_broker_has_no_exchange_or_binding_for_the_invitation_mail_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var management = await ManagementClientAsync(cancellationToken);
        await WaitForBindingAsync(management, MembershipStarted, "compliance", cancellationToken);

        // A sender's exchange is declared on its first publish, so the host must have published a mail request first.
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        var bindings = await BindingsAsync(management, cancellationToken);
        var exchanges = await management.GetFromJsonAsync<List<Exchange>>(new Uri("/api/exchanges/%2F", UriKind.Relative), cancellationToken) ?? [];

        bindings.Where(binding => binding.Source == InvitationMailRequested)
            .Select(binding => $"{binding.Source} -> {binding.DestinationType} {binding.Destination}")
            .ShouldBeEmpty("the broker routes InvitationMailRequestedV1, which Organisation's host publishes and consumes locally, to a queue");
        exchanges.Select(exchange => exchange.Name).ShouldNotContain(InvitationMailRequested, "the broker holds an exchange for InvitationMailRequestedV1");
    }

    /// <summary>
    /// A Wolverine listener dispatches by the message's type, whatever queue it arrived on and whether or not the broker
    /// routes that type there. So a mail request someone with broker credentials publishes straight to any queue the
    /// Organisation service consumes from (its own queue, or a system queue such as Wolverine's request/reply queue)
    /// must not reach the consumer: no link is issued and nothing is mailed.
    /// </summary>
    [Fact]
    public async Task A_mail_request_published_straight_to_a_queue_the_organisation_service_consumes_issues_no_link_and_mails_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var management = await ManagementClientAsync(cancellationToken);
        using var owner = await aspire.CreateHostClientAsync("owner");

        // The service answers once it is up, and so are its listeners.
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var queues = await QueuesOrganisationConsumesAsync(management, cancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"Organisation consumes from: {string.Join(", ", queues)}");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        List<string> handled = [];
        foreach (var queue in queues)
        {
            var address = Invitee();
            var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
            var hash = await database.TokenHashesAsync(membership, cancellationToken);
            hash.ShouldNotBeEmpty("the invitation's own mail issued no link");

            await PublishForgedMailRequestAsync(channel, queue, membership, pharmacy, cancellationToken);

            var (hashAfter, mails) = await WaitForAForgedRunAsync(membership, hash, address, cancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"{queue}: token hash {(hashAfter.SequenceEqual(hash) ? "unchanged" : "changed")}, {mails} mail(s) to the invitee");
            if (!hashAfter.SequenceEqual(hash) || mails != 1)
            {
                handled.Add($"{queue}: token hash {(hashAfter.SequenceEqual(hash) ? "unchanged" : "changed")}, {mails} mail(s) to the invitee");
            }
        }

        handled.ShouldBeEmpty("a mail request published straight to a queue the Organisation service consumes reached its consumer");
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

    /// <summary>
    /// Every queue the Organisation service consumes from, as its own log names them ("Started message listening at
    /// rabbitmq://queue/…"), confirmed against the broker's consumers. Every Wolverine connection is named "Wolverine"
    /// and the request/reply queue <c>wolverine.response.&lt;guid&gt;</c> carries neither the service's name nor its node
    /// id, so the service's log is the one place that says which queues are its.
    /// </summary>
    private async Task<List<string>> QueuesOrganisationConsumesAsync(HttpClient management, CancellationToken cancellationToken)
    {
        var listened = await QueuesOrganisationListensOnAsync(cancellationToken);
        var consumers = await management.GetFromJsonAsync<List<QueueConsumer>>(new Uri("/api/consumers/%2F", UriKind.Relative), cancellationToken) ?? [];
        List<string> queues = [.. consumers.Select(consumer => consumer.Queue.Name).Where(listened.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (queues.Count == 0)
        {
            // Nothing to forge onto: the service listens on no broker queue. That an empty list is not a log that could
            // not be read: no host declares Wolverine's request/reply queue any more, so the broker has no consumer on one.
            consumers.Select(consumer => consumer.Queue.Name).ShouldNotContain(
                name => name.StartsWith("wolverine.response.", StringComparison.Ordinal),
                $"a host still consumes a request/reply queue; consumed: {string.Join(", ", consumers.Select(consumer => consumer.Queue.Name))}");
        }

        return queues;
    }

    /// <summary>
    /// The RabbitMQ queues the Organisation service's log says it started listening on: its log's backlog and what
    /// follows, read until a few seconds after the first such line or for at most <see cref="WaitTimeout"/>.
    /// </summary>
    private async Task<HashSet<string>> QueuesOrganisationListensOnAsync(CancellationToken cancellationToken)
    {
        var logs = aspire.App.Services.GetRequiredService<ResourceLoggerService>();
        var service = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(resource => resource.Name == OrganisationQueue);
        HashSet<string> queues = [];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(WaitTimeout);
        try
        {
            await foreach (var lines in logs.WatchAsync(service).WithCancellation(stop.Token))
            {
                foreach (var line in lines)
                {
                    var match = ListeningAt().Match(line.Content);
                    if (match.Success && queues.Add(match.Groups["queue"].Value) && queues.Count == 1)
                    {
                        stop.CancelAfter(TimeSpan.FromSeconds(3));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The read is over; what it found is the answer.
        }

        return queues;
    }

    [GeneratedRegex("Started message listening at rabbitmq://queue/(?<queue>[^\\s/]+)")]
    private static partial Regex ListeningAt();

    /// <summary>
    /// Publishes a mail request as a Wolverine sender would put it on the wire (the type and the envelope id in the
    /// message's properties, the event as camelCase JSON), but straight to the queue through the default exchange,
    /// past any exchange or binding.
    /// </summary>
    private static async Task PublishForgedMailRequestAsync(IChannel channel, string queue, Guid membership, Guid pharmacy, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Type = InvitationMailRequested,
            ContentType = "application/json",
            MessageId = Guid.CreateVersion7().ToString(),
            DeliveryMode = DeliveryModes.Persistent,
        };
        var message = new
        {
            membershipId = membership,
            pharmacyId = pharmacy,
            metadata = new { eventIdentifier = Guid.CreateVersion7(), occurredAt = TimeProvider.System.GetUtcNow() },
        };

        await channel.BasicPublishAsync(string.Empty, queue, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>
    /// Waits up to <see cref="ForgeryWait"/> for a run of the consumer the forged request would cause, a new token hash
    /// or a second mail, and answers the membership's token hash and the invitee's mails as they are then.
    /// </summary>
    private async Task<(List<string> Hash, int Mails)> WaitForAForgedRunAsync(Guid membership, List<string> hash, string address, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var current = await database.TokenHashesAsync(membership, cancellationToken);
            var mails = await mailpit.CountToAsync(address, cancellationToken);
            if ((!current.SequenceEqual(hash) && mails > 1) || TimeProvider.System.GetElapsedTime(started) >= ForgeryWait)
            {
                return (current, mails);
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Invites the address as an employee through the edge, waits for its one mail, and answers the membership.</summary>
    private async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(
            new Uri($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative),
            new { email = address, jobFunction = "Pta", role = "Employee" },
            cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        var membership = await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem($"the invitation of {address} sent more than one mail");
        return membership;
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>A fresh address per test, so no test sees another's invitation or mail.</summary>
    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

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

    private sealed record QueueConsumer(QueueName Queue);

    private sealed record QueueName(string Name);
}
