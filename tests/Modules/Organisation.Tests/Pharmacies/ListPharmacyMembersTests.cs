using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyMembers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager lists the pharmacy's active members by name, a page at a time; nobody else may.</summary>
public class ListPharmacyMembersTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Cleo = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_lists_the_active_members_by_name_a_page_at_a_time_with_how_many_there_are()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var cleo = TestPharmacies.AddMember(pharmacy, Cleo, MembershipRole.Employee, name: "Cleo Clerk");
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, name: "Anna Berger");
        TestPharmacies.AddMember(pharmacy, Ben, MembershipRole.Employee, name: "Ben Baker");
        pharmacy.Stored(store);

        var listed = await Handler(store).HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, Owner, PageOf(2), PageSizeOf(2)), TestContext.Current.CancellationToken);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.Select(member => member.Name).ShouldBe(["Cleo Clerk", "Olaf Owner"]);
        listed.Value.Items.Select(member => member.MembershipId)
            .ShouldBe([cleo.Value, TestPharmacies.FounderOf(pharmacy).Value]);
        listed.Value.TotalCount.ShouldBe(4);
        listed.Value.Page.ShouldBe(2);
        listed.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Each_member_shows_membership_person_name_job_function_role_and_since()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, name: "Anna Berger");
        pharmacy.Stored(store);

        var listed = await Handler(store).HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.ShouldBe([
            new PharmacyMemberDto(
                anna.Value,
                Anna.Value,
                "Anna Berger",
                OrganisationJobFunction.Pta,
                OrganisationMembershipRole.Employee,
                TestPharmacies.RegisteredAt.Value),
            new PharmacyMemberDto(
                TestPharmacies.FounderOf(pharmacy).Value,
                Owner.Value,
                "Olaf Owner",
                OrganisationJobFunction.Pharmacist,
                OrganisationMembershipRole.Manager,
                TestPharmacies.RegisteredAt.Value),
        ]);
    }

    [Fact]
    public async Task Invited_ended_and_revoked_memberships_are_no_members()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee, name: "Anna Berger");
        var ben = TestPharmacies.AddMember(pharmacy, Ben, MembershipRole.Employee, name: "Ben Baker");
        pharmacy.EndMembership(ben, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        TestPharmacies.Invite(pharmacy, "invited@example.org");
        var revoked = TestPharmacies.Invite(pharmacy, "revoked@example.org");
        pharmacy.RevokeInvitation(revoked).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);

        var listed = await Handler(store).HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.Select(member => member.MembershipId)
            .ShouldBe([anna.Value, TestPharmacies.FounderOf(pharmacy).Value]);
        listed.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_employee_may_not_list_the_members()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);

        var refused = await Handler(store).HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, Anna, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_without_an_active_membership_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var ben = TestPharmacies.AddMember(pharmacy, Ben, MembershipRole.Manager);
        pharmacy.EndMembership(ben, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var handler = Handler(store);

        var stranger = await handler.HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, PersonId.From(Guid.CreateVersion7()), Page.First, PageSize.Default), TestContext.Current.CancellationToken);
        var ended = await handler.HandleAsync(
            new ListPharmacyMembers(pharmacy.Id, Ben, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        stranger.IsSuccess.ShouldBeFalse();
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        ended.IsSuccess.ShouldBeFalse();
        ended.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        ended.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_pharmacy_is_not_found()
    {
        var refused = await Handler(new InMemoryPharmacyStore()).HandleAsync(
            new ListPharmacyMembers(PharmacyId.New(), Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static ListPharmacyMembersHandler Handler(InMemoryPharmacyStore store) => new(store);

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
