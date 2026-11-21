using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Registering a pharmacy through the running application: the founder reads it back as its manager and finds it among
/// their pharmacies, nobody else sees it, and the outbox announces it to the other services through the broker.
/// </summary>
public class OrganisationPharmaciesTests(AspireFixture aspire)
{
    private const string PharmacyRegisteredExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.PharmacyRegisteredV1";
    private const string MembershipStartedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);
    private static readonly Uri Mine = new("/organisation/pharmacies/mine?page=1&pageSize=100", UriKind.Relative);
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_registered_pharmacy_is_read_back_by_its_founder_as_its_manager_and_listed_among_theirs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        var name = $"Apotheke {Guid.CreateVersion7()}";

        using var created = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        created.Headers.Location.ShouldBe(new Uri($"/organisation/pharmacies/{id}", UriKind.Relative));
        var pharmacy = await client.GetFromJsonAsync<MyPharmacy>(created.Headers.Location, cancellationToken);
        pharmacy.ShouldNotBeNull();
        pharmacy.Id.ShouldBe(id);
        pharmacy.Name.ShouldBe(name);
        pharmacy.Role.ShouldBe("Manager");
        pharmacy.JobFunction.ShouldBe("Pharmacist");
        pharmacy.CanManageMembers.ShouldBeTrue();
        pharmacy.CanAssignCourses.ShouldBeTrue();
        var mine = await client.GetFromJsonAsync<MyPharmacyPage>(Mine, cancellationToken);
        mine!.Items.ShouldContain(pharmacy);
        mine.PageSize.ShouldBe(100);
    }

    [Fact]
    public async Task Someone_who_is_no_member_neither_reads_nor_lists_the_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var id = await RegisterAsync(owner, $"Apotheke {Guid.CreateVersion7()}", cancellationToken);

        using var read = await visitor.GetAsync(new Uri($"/organisation/pharmacies/{id}", UriKind.Relative), cancellationToken);
        var listed = await visitor.GetFromJsonAsync<MyPharmacyPage>(Mine, cancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await read.Content.ReadFromJsonAsync<Problem>(cancellationToken))!.Code.ShouldBe("organisation.pharmacy-not-found");
        listed!.Items.ShouldNotContain(pharmacy => pharmacy.Id == id);
    }

    [Fact]
    public async Task A_blank_name_is_refused_as_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");

        using var refused = await client.PostAsJsonAsync(Pharmacies, new { name = "  ", jobFunction = "Pharmacist" }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<Problem>(cancellationToken))!.Code.ShouldBe("organisation.invalid-pharmacy-name");
    }

    [Fact]
    public async Task A_missing_job_function_is_refused_as_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");

        using var refused = await client.PostAsJsonAsync(Pharmacies, new { name = "Stadt-Apotheke" }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<Problem>(cancellationToken))!.Code.ShouldBe("organisation.invalid-job-function");
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_refused()
    {
        using var client = await aspire.CreateHostClientAsync("owner");

        using var response = await client.GetAsync(new Uri("/organisation/pharmacies/mine?pageSize=101", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_anonymous_caller_neither_registers_lists_nor_reads_pharmacies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var registered = await client.PostAsJsonAsync(Pharmacies, new { name = "Anonyme Apotheke", jobFunction = "Pharmacist" }, cancellationToken);
        using var listed = await client.GetAsync(Mine, cancellationToken);
        using var read = await client.GetAsync(new Uri($"/organisation/pharmacies/{Guid.CreateVersion7()}", UriKind.Relative), cancellationToken);

        registered.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        listed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registering_announces_the_pharmacy_and_the_founders_membership_through_the_broker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        var factory = new ConnectionFactory { Uri = new Uri(await aspire.App.GetConnectionStringAsync("messaging", cancellationToken) ?? throw new InvalidOperationException("The broker has no connection string.")) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);

        // Wolverine declares an event's exchange when it first sends one: register once so both exist, then listen.
        await RegisterAsync(client, $"Aufwärm-Apotheke {Guid.CreateVersion7()}", cancellationToken);
        await WaitForExchangeAsync(connection, PharmacyRegisteredExchange, cancellationToken);
        await WaitForExchangeAsync(connection, MembershipStartedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var pharmacyRegisteredQueue = await BindQueueAsync(channel, PharmacyRegisteredExchange, cancellationToken);
        var membershipStartedQueue = await BindQueueAsync(channel, MembershipStartedExchange, cancellationToken);
        var name = $"Apotheke {Guid.CreateVersion7()}";

        var id = await RegisterAsync(client, name, cancellationToken);

        var pharmacy = await client.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{id}", UriKind.Relative), cancellationToken);
        var pharmacyRegistered = await ReceiveAsync<PharmacyRegistered>(channel, pharmacyRegisteredQueue, received => received.PharmacyId == id, cancellationToken);
        var membershipStarted = await ReceiveAsync<MembershipStarted>(channel, membershipStartedQueue, received => received.PharmacyId == id, cancellationToken);
        pharmacyRegistered.Name.ShouldBe(name);
        membershipStarted.MembershipId.ShouldBe(pharmacy!.MembershipId);
        membershipStarted.DisplayName.ShouldBe("Olaf Owner");
        membershipStarted.JobFunction.ShouldBe("Pharmacist");
        membershipStarted.Role.ShouldBe("Manager");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>Asks for the exchange without declaring it, so the test never conflicts with the arguments Wolverine gives it.</summary>
    private static async Task WaitForExchangeAsync(IConnection connection, string exchange, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(EventTimeout);
        while (true)
        {
            // A passive declare of a missing exchange closes its channel, so every attempt gets a channel of its own.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, timeout.Token);
                return;
            }
            catch (OperationInterruptedException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
            }
        }
    }

    /// <summary>A queue of the test's own, named by the broker and gone with the connection, bound to the event's exchange.</summary>
    private static async Task<string> BindQueueAsync(IChannel channel, string exchange, CancellationToken cancellationToken)
    {
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken);

        // Wolverine's exchanges fan out; "#" also matches every routing key should one be a topic exchange.
        await channel.QueueBindAsync(queue.QueueName, exchange, routingKey: "#", cancellationToken: cancellationToken);
        return queue.QueueName;
    }

    private static async Task<T> ReceiveAsync<T>(IChannel channel, string queue, Func<T, bool> wanted, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(EventTimeout);
        while (true)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true, timeout.Token);
            if (delivery is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
                continue;
            }

            var received = JsonSerializer.Deserialize<T>(delivery.Body.Span, JsonSerializerOptions.Web);
            if (received is not null && wanted(received))
            {
                return received;
            }
        }
    }

    private sealed record MyPharmacy(Guid Id, string Name, Guid MembershipId, string Role, string JobFunction, bool CanManageMembers, bool CanAssignCourses);

    private sealed record MyPharmacyPage(IReadOnlyList<MyPharmacy> Items, int TotalCount, int Page, int PageSize);

    private sealed record Problem(string Code);

    private sealed record PharmacyRegistered(Guid PharmacyId, string Name);

    private sealed record MembershipStarted(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt);
}
