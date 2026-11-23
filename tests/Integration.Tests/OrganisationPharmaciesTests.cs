using System.Net;
using System.Net.Http.Json;

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

    private readonly OrganisationDatabase database = new(aspire);

    /// <summary>
    /// The founder, <c>owner</c>, signs in with an address Keycloak has verified (<see cref="RealmUsersTests"/>), so this is
    /// also the verified caller's registration.
    /// </summary>

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
    public async Task An_account_whose_address_keycloak_has_not_verified_cannot_register_a_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("unverified");
        var name = $"Apotheke {Guid.CreateVersion7()}";

        using var refused = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<Problem>(cancellationToken))!.Code.ShouldBe("organisation.email-not-verified");
        var mine = await client.GetFromJsonAsync<MyPharmacyPage>(Mine, cancellationToken);
        mine!.Items.ShouldNotContain(pharmacy => pharmacy.Name == name);
        (await database.PharmaciesNamedAsync(name, cancellationToken)).ShouldBe(0);
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

    [Theory]
    [InlineData("Stadt\r\nApotheke")]
    [InlineData("Stadt\tApotheke")]
    [InlineData("Stadt\u202EekehtopA")]
    [InlineData("Stadt\u2066Apotheke")]
    public async Task A_name_with_a_control_or_bidi_character_is_refused_as_the_callers_mistake(string name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");

        using var refused = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);

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

    [Theory]
    [InlineData("42")]
    [InlineData("\"42\"")]
    public async Task A_job_function_that_is_no_known_one_is_refused_as_the_callers_mistake(string jobFunction)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("owner");
        using var body = new StringContent($"{{\"name\":\"Stadt-Apotheke\",\"jobFunction\":{jobFunction}}}", System.Text.Encoding.UTF8, "application/json");

        using var refused = await client.PostAsync(Pharmacies, body, cancellationToken);

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
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);

        // Wolverine declares an event's exchange when it first sends one: register once so both exist, then listen.
        await RegisterAsync(client, $"Aufwärm-Apotheke {Guid.CreateVersion7()}", cancellationToken);
        await Broker.WaitForExchangeAsync(connection, PharmacyRegisteredExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, MembershipStartedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var pharmacyRegisteredQueue = await Broker.BindQueueAsync(channel, PharmacyRegisteredExchange, cancellationToken);
        var membershipStartedQueue = await Broker.BindQueueAsync(channel, MembershipStartedExchange, cancellationToken);
        var name = $"Apotheke {Guid.CreateVersion7()}";

        var id = await RegisterAsync(client, name, cancellationToken);

        var pharmacy = await client.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{id}", UriKind.Relative), cancellationToken);
        var pharmacyRegistered = await Broker.ReceiveAsync<PharmacyRegistered>(channel, pharmacyRegisteredQueue, received => received.PharmacyId == id, cancellationToken);
        var membershipStarted = await Broker.ReceiveAsync<MembershipStarted>(channel, membershipStartedQueue, received => received.PharmacyId == id, cancellationToken);
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

    private sealed record MyPharmacy(Guid Id, string Name, Guid MembershipId, string Role, string JobFunction, bool CanManageMembers, bool CanAssignCourses);

    private sealed record MyPharmacyPage(IReadOnlyList<MyPharmacy> Items, int TotalCount, int Page, int PageSize);

    private sealed record Problem(string Code);

    private sealed record PharmacyRegistered(Guid PharmacyId, string Name);

    private sealed record MembershipStarted(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt);
}
