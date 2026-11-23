using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListPharmacyMembers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

/// <summary>
/// The assign wizard's member list: the pharmacy's active members as they stand now, one row per person, filtered by
/// their current job function, sorted by name and paged; only for a caller whose current role grants assigning courses
/// (docs/adr/0003, docs/adr/0015).
/// </summary>
public class ListPharmacyMembersTests
{
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_lists_the_active_members_sorted_by_name()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        var berta = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, berta, Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
        var anna = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, anna, Pharmacy, MembershipRole.Employee, "anna Arndt", JobFunction.Pta);
        var clara = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, clara, Pharmacy, MembershipRole.Employee, "Clara Cramer", JobFunction.Trainee);

        var result = await ListAsync(store, Manager);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(4);
        result.Value.Items.ShouldBe(
        [
            new ComplianceMemberDto(anna.Value, "anna Arndt", ComplianceJobFunction.Pta),
            new ComplianceMemberDto(berta.Value, "Berta Braun", ComplianceJobFunction.Pka),
            new ComplianceMemberDto(clara.Value, "Clara Cramer", ComplianceJobFunction.Trainee),
            new ComplianceMemberDto(Manager.Value, "Mara Manager", ComplianceJobFunction.Pharmacist),
        ]);
    }

    [Fact]
    public async Task The_filter_keeps_only_members_with_that_job_function()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        var anna = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        await ComplianceMembers.StartAsync(store, PersonId.From(Guid.CreateVersion7()), Pharmacy, MembershipRole.Employee, "Berta Braun", JobFunction.Pka);
        var clara = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, clara, Pharmacy, MembershipRole.Employee, "Clara Cramer", JobFunction.Pta);

        var result = await ListAsync(store, Manager, JobFunction.Pta);

        result.Value.Items.Select(row => row.PersonId).ShouldBe([anna.Value, clara.Value]);
        result.Value.Items.ShouldAllBe(row => row.JobFunction == ComplianceJobFunction.Pta);
        result.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_filter_applies_to_the_current_job_function_not_the_one_the_membership_started_with()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Other);
        var anna = PersonId.From(Guid.CreateVersion7());
        var membership = await ComplianceMembers.StartAsync(store, anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        await ComplianceMembers.ChangeAsync(store, membership, anna, Pharmacy, 2, MembershipRole.Employee, "Anna Arndt", JobFunction.Pharmacist);

        var pharmacists = await ListAsync(store, Manager, JobFunction.Pharmacist);
        var ptas = await ListAsync(store, Manager, JobFunction.Pta);

        pharmacists.Value.Items.ShouldHaveSingleItem().ShouldBe(new ComplianceMemberDto(anna.Value, "Anna Arndt", ComplianceJobFunction.Pharmacist));
        ptas.Value.Items.ShouldBeEmpty();
        ptas.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_changed_name_is_listed_and_sorted_under_the_latest_version()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager", JobFunction.Pharmacist);
        var anna = PersonId.From(Guid.CreateVersion7());
        var membership = await ComplianceMembers.StartAsync(store, anna, Pharmacy, MembershipRole.Employee, "Anna Arndt", JobFunction.Pta);
        await ComplianceMembers.ChangeAsync(store, membership, anna, Pharmacy, 3, MembershipRole.Employee, "Zoe Zander", JobFunction.Pta);
        await ComplianceMembers.ChangeAsync(store, membership, anna, Pharmacy, 2, MembershipRole.Employee, "Berta Braun", JobFunction.Pta);

        var result = await ListAsync(store, Manager);

        result.Value.Items.Select(row => row.Name).ShouldBe(["Mara Manager", "Zoe Zander"]);
        result.Value.Items[1].PersonId.ShouldBe(anna.Value);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_is_not_listed()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        var leaver = PersonId.From(Guid.CreateVersion7());
        var membership = await ComplianceMembers.StartAsync(store, leaver, Pharmacy, MembershipRole.Employee, "Anna Arndt");
        await ComplianceMembers.EndAsync(store, membership, leaver, Pharmacy);

        var result = await ListAsync(store, Manager);

        result.Value.Items.ShouldHaveSingleItem().PersonId.ShouldBe(Manager.Value);
        result.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Members_of_another_pharmacy_are_not_listed()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        await ComplianceMembers.StartAsync(store, PersonId.From(Guid.CreateVersion7()), PharmacyId.From(Guid.CreateVersion7()), MembershipRole.Employee, "Anna Arndt");

        var result = await ListAsync(store, Manager);

        result.Value.Items.ShouldHaveSingleItem().PersonId.ShouldBe(Manager.Value);
        result.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_person_with_two_active_memberships_is_listed_once_from_the_newest()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        var anna = PersonId.From(Guid.CreateVersion7());
        var older = MembershipId.From(Guid.CreateVersion7());
        var newer = MembershipId.From(Guid.CreateVersion7());
        newer.Value.CompareTo(older.Value).ShouldBePositive();
        await ComplianceMembers.StartAsync(store, newer, anna, Pharmacy, MembershipRole.Employee, "Anna Neu", JobFunction.Pharmacist);
        await ComplianceMembers.StartAsync(store, older, anna, Pharmacy, MembershipRole.Employee, "Anna Alt", JobFunction.Pta);

        var result = await ListAsync(store, Manager);

        result.Value.TotalCount.ShouldBe(2);
        result.Value.Items.Where(row => row.PersonId == anna.Value).ShouldHaveSingleItem()
            .ShouldBe(new ComplianceMemberDto(anna.Value, "Anna Neu", ComplianceJobFunction.Pharmacist));
    }

    [Fact]
    public async Task Members_with_the_same_name_are_ordered_by_person_id()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        var first = PersonId.From(Guid.CreateVersion7());
        var second = PersonId.From(Guid.CreateVersion7());
        first.Value.CompareTo(second.Value).ShouldBeNegative();
        await ComplianceMembers.StartAsync(store, second, Pharmacy, MembershipRole.Employee, "Anna Arndt");
        await ComplianceMembers.StartAsync(store, first, Pharmacy, MembershipRole.Employee, "Anna Arndt");

        var result = await ListAsync(store, Manager);

        result.Value.Items.Select(row => row.PersonId).ShouldBe([first.Value, second.Value, Manager.Value]);
    }

    [Fact]
    public async Task The_page_skips_and_takes_and_counts_the_whole_filtered_list()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Erna Ebert");
        foreach (var name in new[] { "Dora Dietz", "Anna Arndt", "Clara Cramer", "Berta Braun" })
        {
            await ComplianceMembers.StartAsync(store, PersonId.From(Guid.CreateVersion7()), Pharmacy, MembershipRole.Employee, name);
        }

        var result = await ListAsync(store, Manager, page: PageOf(2), pageSize: PageSizeOf(2));

        result.Value.Items.Select(row => row.Name).ShouldBe(["Clara Cramer", "Dora Dietz"]);
        result.Value.TotalCount.ShouldBe(5);
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_with_the_total()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        await ComplianceMembers.StartAsync(store, PersonId.From(Guid.CreateVersion7()), Pharmacy, MembershipRole.Employee, "Anna Arndt");

        var result = await ListAsync(store, Manager, page: PageOf(3), pageSize: PageSizeOf(2));

        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(2);
        result.Value.Page.ShouldBe(3);
        result.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task A_non_member_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");

        var result = await ListAsync(store, PersonId.From(Guid.CreateVersion7()));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        await ComplianceMembers.StartAsync(store, PersonId.From(Guid.CreateVersion7()), Pharmacy, MembershipRole.Employee, "Anna Arndt");
        await ComplianceMembers.EndAsync(store, membership, Manager, Pharmacy);

        var result = await ListAsync(store, Manager);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_employee_lacks_the_capability()
    {
        var store = new InMemoryMemberStore();
        var employee = PersonId.From(Guid.CreateVersion7());
        await ComplianceMembers.StartAsync(store, employee, Pharmacy, MembershipRole.Employee, "Anna Arndt");

        var result = await ListAsync(store, employee);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_manager_demoted_by_a_later_change_lacks_the_capability()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Manager, Pharmacy, MembershipRole.Manager, "Mara Manager");
        await ComplianceMembers.ChangeAsync(store, membership, Manager, Pharmacy, 2, MembershipRole.Employee, "Mara Manager");

        var result = await ListAsync(store, Manager);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("compliance.capability-missing");
        result.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_employee_promoted_by_a_later_change_may_list()
    {
        var store = new InMemoryMemberStore();
        var anna = PersonId.From(Guid.CreateVersion7());
        var membership = await ComplianceMembers.StartAsync(store, anna, Pharmacy, MembershipRole.Employee, "Anna Arndt");
        await ComplianceMembers.ChangeAsync(store, membership, anna, Pharmacy, 2, MembershipRole.Manager, "Anna Arndt");

        var result = await ListAsync(store, anna);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldHaveSingleItem().PersonId.ShouldBe(anna.Value);
    }

    private static Task<Result<PagedResult<ComplianceMemberDto>, ApiError>> ListAsync(
        InMemoryMemberStore store,
        PersonId by,
        JobFunction? jobFunction = null,
        Page? page = null,
        PageSize? pageSize = null) =>
        new ListPharmacyMembersHandler(store).HandleAsync(
            new ListPharmacyMembers(Pharmacy, by, jobFunction, page ?? Page.First, pageSize ?? PageSize.Default),
            TestContext.Current.CancellationToken);

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
