using System.Net;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The assign wizard's member list through the edge, <c>GET /compliance/pharmacies/{id}/members</c> (#217): the
/// pharmacy's active members from Compliance's own copy of the memberships, as they stand now, by name, filterable by
/// job function and paged; only for a caller whose current role there grants assigning courses (docs/adr/0003,
/// docs/adr/0015). Members reach Compliance as their events, delivered and awaited one after the other; each test has a
/// pharmacy of its own, where developer is the manager "Mara Manager", employee-a the Pta "Anna Arndt" and employee-b
/// the Pka "Berta Braun".
/// </summary>
public class ComplianceMembersTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string ChangedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";

    private readonly OrganisationDatabase organisation = new(aspire);

    [Fact]
    public async Task A_manager_of_the_pharmacy_lists_its_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);

        var page = await PageOfAsync(response, cancellationToken);
        page.Items.ShouldBe(
        [
            new MemberRow(setting.EmployeeA.PersonId, "Anna Arndt", "Pta"),
            new MemberRow(setting.EmployeeB.PersonId, "Berta Braun", "Pka"),
            new MemberRow(setting.Manager.PersonId, "Mara Manager", "Pharmacist"),
        ]);
        page.TotalCount.ShouldBe(3);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task An_employee_of_the_pharmacy_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var employee = await aspire.CreateHostClientAsync("employee-a");

        using var response = await employee.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);
    }

    [Fact]
    public async Task A_signed_in_non_member_is_told_the_pharmacy_does_not_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var response = await visitor.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "compliance.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();

        using var response = await anonymous.GetAsync(MembersUri(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task The_list_filters_by_job_function()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MembersUri(setting.Pharmacy, "?jobFunction=Pta"), cancellationToken);

        var page = await PageOfAsync(response, cancellationToken);
        page.Items.ShouldHaveSingleItem().ShouldBe(new MemberRow(setting.EmployeeA.PersonId, "Anna Arndt", "Pta"));
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_job_function_number_outside_the_enum_is_the_callers_mistake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MembersUri(setting.Pharmacy, "?jobFunction=7"), cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "compliance.invalid-job-function", cancellationToken);
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        using var response = await manager.GetAsync(MembersUri(setting.Pharmacy, "?page=2&pageSize=2"), cancellationToken);

        var page = await PageOfAsync(response, cancellationToken);
        page.Items.ShouldHaveSingleItem().ShouldBe(new MemberRow(setting.Manager.PersonId, "Mara Manager", "Pharmacist"));
        page.TotalCount.ShouldBe(3);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_drops_out_once_the_ending_is_applied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var before = await manager.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);
        (await PageOfAsync(before, cancellationToken)).TotalCount.ShouldBe(3);

        await DeliverAsync(EndedType, Ended(setting.EmployeeB), cancellationToken);
        using var after = await manager.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);

        var page = await PageOfAsync(after, cancellationToken);
        page.Items.Select(row => row.PersonId).ShouldBe([setting.EmployeeA.PersonId, setting.Manager.PersonId]);
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_changed_job_function_is_listed_once_the_change_is_applied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");

        await DeliverAsync(ChangedType, Changed(setting.EmployeeA, "Pharmacist", "Employee", 2), cancellationToken);
        using var pharmacistsResponse = await manager.GetAsync(MembersUri(setting.Pharmacy, "?jobFunction=Pharmacist"), cancellationToken);
        using var ptasResponse = await manager.GetAsync(MembersUri(setting.Pharmacy, "?jobFunction=Pta"), cancellationToken);

        var pharmacists = await PageOfAsync(pharmacistsResponse, cancellationToken);
        pharmacists.Items.ShouldContain(new MemberRow(setting.EmployeeA.PersonId, "Anna Arndt", "Pharmacist"));
        pharmacists.TotalCount.ShouldBe(2);
        var ptas = await PageOfAsync(ptasResponse, cancellationToken);
        ptas.Items.ShouldBeEmpty();
        ptas.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_manager_demoted_loses_access_once_the_change_is_applied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setting = await PharmacyAsync(cancellationToken);
        using var manager = await aspire.CreateHostClientAsync("developer");
        using var before = await manager.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);
        (await PageOfAsync(before, cancellationToken)).TotalCount.ShouldBe(3);

        await DeliverAsync(ChangedType, Changed(setting.Manager, "Pharmacist", "Employee", 2), cancellationToken);
        using var after = await manager.GetAsync(MembersUri(setting.Pharmacy), cancellationToken);

        await ShouldBeRefusedAsync(after, HttpStatusCode.Forbidden, "compliance.capability-missing", cancellationToken);
    }

    private static Uri MembersUri(Guid pharmacy, string query = "") => new($"/compliance/pharmacies/{pharmacy}/members{query}", UriKind.Relative);

    private static async Task<MemberPage> PageOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<MemberPage>(body, JsonSerializerOptions.Web).ShouldNotBeNull();
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, body);
        var problem = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        (problem?.Code).ShouldBe(code, $"{(int)response.StatusCode} with body: {body}");
    }

    private static StartedEvent Started(Guid personId, Guid pharmacyId, string displayName, string jobFunction, string role) =>
        new(Guid.CreateVersion7(), personId, pharmacyId, displayName, jobFunction, role, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EndedEvent Ended(StartedEvent started) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static ChangedEvent Changed(StartedEvent started, string jobFunction, string role, int version) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, started.DisplayName, jobFunction, role, version, NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>A pharmacy of its own in Compliance: developer as its manager, employee-a and employee-b as its employees.</summary>
    private async Task<Setting> PharmacyAsync(CancellationToken cancellationToken)
    {
        var pharmacy = Guid.CreateVersion7();
        var manager = Started(await organisation.SubjectOfAsync("developer"), pharmacy, "Mara Manager", "Pharmacist", "Manager");
        var employeeA = Started(await organisation.SubjectOfAsync("employee-a"), pharmacy, "Anna Arndt", "Pta", "Employee");
        var employeeB = Started(await organisation.SubjectOfAsync("employee-b"), pharmacy, "Berta Braun", "Pka", "Employee");
        await DeliverAsync(StartedType, manager, cancellationToken);
        await DeliverAsync(StartedType, employeeA, cancellationToken);
        await DeliverAsync(StartedType, employeeB, cancellationToken);
        return new Setting(pharmacy, manager, employeeA, employeeB);
    }

    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private sealed record Setting(Guid Pharmacy, StartedEvent Manager, StartedEvent EmployeeA, StartedEvent EmployeeB);

    private sealed record MemberPage(IReadOnlyList<MemberRow> Items, int TotalCount, int Page, int PageSize);

    private sealed record MemberRow(Guid PersonId, string Name, string JobFunction);

    private sealed record Problem(string Code);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record ChangedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version, EventMetadata Metadata);
}
