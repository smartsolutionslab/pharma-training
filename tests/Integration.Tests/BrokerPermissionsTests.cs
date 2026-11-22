using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What the running broker lets a service's own user do (#183), tried with Catalog's credentials as its service holds them:
/// it may not bind its queue to the test definition, which carries every solution, read another service's queue, publish
/// another module's event or straight to a queue, or declare a queue not its own. Every attempt that the broker allows
/// (as it does while every service is its admin) is undone at once, so the shared application is left as it was.
/// </summary>
public class BrokerPermissionsTests(AspireFixture aspire)
{
    private const string Catalog = "catalog";

    private const string Compliance = "compliance";

    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string TestDefinitionPublished = Contracts + "Editorial.TestDefinitionPublishedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Catalog_may_not_bind_its_queue_to_the_test_definition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestDefinitionExchangeExistsAsync(cancellationToken);
        await using var catalog = await CatalogConnectionAsync(cancellationToken);

        ShutdownEventArgs? refusal;
        try
        {
            refusal = await BrokerUsers.RefusalOfAsync(catalog, channel => channel.QueueBindAsync(Catalog, TestDefinitionPublished, string.Empty, cancellationToken: cancellationToken), cancellationToken);
        }
        finally
        {
            // Allowed, the binding would route every solution to Catalog: the admin removes it again at once.
            await using var admin = await Broker.ConnectAsync(aspire, cancellationToken);
            await using var channel = await admin.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.QueueUnbindAsync(Catalog, TestDefinitionPublished, string.Empty, cancellationToken: cancellationToken);
        }

        ShouldBeRefused(refusal, $"binding the queue {Catalog} to {TestDefinitionPublished}");
    }

    [Fact]
    public async Task Catalog_may_not_read_compliances_queue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var catalog = await CatalogConnectionAsync(cancellationToken);

        var refusal = await BrokerUsers.RefusalOfAsync(
            catalog,
            async channel =>
            {
                // Allowed, a message taken would be Compliance's: it goes back on its queue unacknowledged.
                var delivery = await channel.BasicGetAsync(Compliance, autoAck: false, cancellationToken);
                if (delivery is not null)
                {
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                }
            },
            cancellationToken);

        ShouldBeRefused(refusal, $"reading the queue {Compliance}");
    }

    [Fact]
    public async Task Catalog_may_not_publish_the_test_definition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestDefinitionExchangeExistsAsync(cancellationToken);
        await using var catalog = await CatalogConnectionAsync(cancellationToken);

        // Allowed, it reaches only queues of tests that listen for test definitions, which skip a version they did not publish.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { courseVersionId = Guid.CreateVersion7() }, JsonSerializerOptions.Web);
        var refusal = await BrokerUsers.RefusalOfAsync(
            catalog,
            channel => channel.BasicPublishAsync(TestDefinitionPublished, string.Empty, mandatory: false, new BasicProperties { ContentType = "application/json" }, body, cancellationToken).AsTask(),
            cancellationToken);

        ShouldBeRefused(refusal, $"publishing to {TestDefinitionPublished}");
    }

    [Fact]
    public async Task Catalog_may_not_publish_straight_to_a_queue_through_the_default_exchange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var catalog = await CatalogConnectionAsync(cancellationToken);

        // Allowed, a routing key no queue has drops the message.
        var nowhere = $"broker-permissions-{Guid.CreateVersion7():N}";
        var refusal = await BrokerUsers.RefusalOfAsync(
            catalog,
            channel => channel.BasicPublishAsync(string.Empty, nowhere, mandatory: false, new BasicProperties(), "{}"u8.ToArray(), cancellationToken).AsTask(),
            cancellationToken);

        ShouldBeRefused(refusal, "publishing through the default exchange");
    }

    [Fact]
    public async Task Catalog_may_not_declare_a_queue_other_than_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var catalog = await CatalogConnectionAsync(cancellationToken);
        var other = $"{Catalog}-broker-permissions-{Guid.CreateVersion7():N}";

        // Allowed, the queue is exclusive and goes with the channel's connection; it is deleted at once besides.
        var refusal = await BrokerUsers.RefusalOfAsync(
            catalog,
            async channel =>
            {
                await channel.QueueDeclareAsync(other, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);
                await channel.QueueDeleteAsync(other, cancellationToken: cancellationToken);
            },
            cancellationToken);

        ShouldBeRefused(refusal, $"declaring the queue {other}");
    }

    [Fact]
    public async Task Every_wolverine_connection_uses_the_user_of_a_service_and_every_service_has_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var adminUser = BrokerUsers.UserOf(await BrokerUsers.AdminConnectionStringAsync(aspire, cancellationToken));
        var modules = BrokerUsers.ServiceHosts(aspire).Select(service => service.Name).Order(StringComparer.Ordinal).ToList();
        using var management = await BrokerUsers.ManagementClientAsync(aspire, cancellationToken);

        var connections = await WolverineConnectionsAsync(management, modules.Count, cancellationToken);

        connections.Select(connection => connection.User).ShouldNotContain(adminUser, $"A service's Wolverine connects as the admin user '{adminUser}'.");
        connections.Select(connection => connection.User).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ShouldBe(modules, ignoreOrder: false, "Every service's Wolverine should connect as its module's user, and as no other.");
    }

    [Fact]
    public async Task Every_service_user_reads_exactly_the_exchanges_bound_to_its_queue_and_writes_only_its_own_modules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var modules = BrokerUsers.ServiceHosts(aspire).Select(service => service.Name).ToList();
        using var management = await BrokerUsers.ManagementClientAsync(aspire, cancellationToken);
        await WaitForBindingAsync(management, Contracts + "Organisation.MembershipStartedV1", Compliance, cancellationToken);

        var permissions = await management.GetFromJsonAsync<List<Permission>>(new Uri("/api/permissions", UriKind.Relative), cancellationToken) ?? [];
        var exchanges = (await management.GetFromJsonAsync<List<Exchange>>(new Uri("/api/exchanges/%2F", UriKind.Relative), cancellationToken) ?? [])
            .Select(exchange => exchange.Name)
            .Where(name => name.StartsWith(Contracts, StringComparison.Ordinal))
            .ToList();
        var bindings = await management.GetFromJsonAsync<List<Binding>>(new Uri("/api/bindings/%2F", UriKind.Relative), cancellationToken) ?? [];

        modules.ShouldNotBeEmpty();
        exchanges.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var permission = permissions.Where(candidate => candidate.User == module && candidate.Vhost == "/").ShouldHaveSingleItem($"The broker gives '{module}' no permission of its own.");
            var bound = bindings.Where(binding => binding.Destination == module && binding.DestinationType == "queue" && binding.Source.Length > 0)
                .Select(binding => binding.Source)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            exchanges.Where(exchange => Matches(permission.Read, exchange)).Order(StringComparer.Ordinal).ToList()
                .ShouldBe(bound, ignoreOrder: false, $"{module} may read ({permission.Read}) other exchanges than those bound to its queue.");
            exchanges.Where(exchange => Matches(permission.Write, exchange))
                .ShouldAllBe(exchange => exchange.StartsWith($"{Contracts}{module}.", StringComparison.OrdinalIgnoreCase), $"{module} may write ({permission.Write}) another module's exchange.");
        }
    }

    private static void ShouldBeRefused(ShutdownEventArgs? refusal, string attempt)
    {
        refusal.ShouldNotBeNull($"The broker let Catalog's credentials go on {attempt}.");
        refusal.ReplyCode.ShouldBe(BrokerUsers.AccessRefused, $"{attempt} failed with {refusal.ReplyCode} {refusal.ReplyText}, not ACCESS_REFUSED.");
        refusal.ReplyText.ShouldStartWith("ACCESS_REFUSED");
    }

    /// <summary>A connection with the credentials Catalog's service holds.</summary>
    private async Task<IConnection> CatalogConnectionAsync(CancellationToken cancellationToken) =>
        await BrokerUsers.ConnectAsync(await BrokerUsers.ConnectionStringOfAsync(aspire, Catalog, cancellationToken), cancellationToken);

    /// <summary>
    /// The test definition's exchange, which Editorial's Wolverine declares on its first send: a version is published, so a
    /// refusal below is not the broker missing the exchange.
    /// </summary>
    private async Task TestDefinitionExchangeExistsAsync(CancellationToken cancellationToken)
    {
        using var editor = await aspire.CreateHostClientAsync("editor");
        await EditorialWorkflow.PublishedAsync(editor, cancellationToken);
        await using var admin = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(admin, TestDefinitionPublished, cancellationToken);
    }

    /// <summary>The broker's Wolverine connections, once every service has opened one.</summary>
    private static async Task<List<Connection>> WolverineConnectionsAsync(HttpClient management, int services, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var connections = (await management.GetFromJsonAsync<List<Connection>>(new Uri("/api/connections", UriKind.Relative), cancellationToken) ?? [])
                .Where(connection => connection.ClientProperties?.ConnectionName == "Wolverine")
                .ToList();
            if (connections.Select(connection => connection.User).Distinct(StringComparer.Ordinal).Count() >= services
                || TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                return connections;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task WaitForBindingAsync(HttpClient management, string exchange, string queue, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var bindings = await management.GetFromJsonAsync<List<Binding>>(new Uri("/api/bindings/%2F", UriKind.Relative), cancellationToken) ?? [];
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

    private static bool Matches(string pattern, string name) =>
        Regex.IsMatch(name, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

    private sealed record Permission(string User, string Vhost, string Configure, string Write, string Read);

    private sealed record Exchange(string Name);

    private sealed record Binding(
        string Source,
        string Destination,
        [property: JsonPropertyName("destination_type")] string DestinationType);

    private sealed record Connection(
        string User,
        [property: JsonPropertyName("client_properties")] ClientProperties? ClientProperties);

    private sealed record ClientProperties([property: JsonPropertyName("connection_name")] string? ConnectionName);
}
