using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyInvitations;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager lists the pharmacy's invitations nobody accepted or revoked, open or run out, by expiry; nobody else may.</summary>
public class ListPharmacyInvitationsTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_lists_open_and_expired_invitations_by_expiry()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var expired = TestPharmacies.Invite(pharmacy, "expired@example.org", TestPharmacies.RegisteredAt.PlusDays(-20));
        var latest = TestPharmacies.Invite(pharmacy, "latest@example.org", TestPharmacies.RegisteredAt);
        var sooner = TestPharmacies.Invite(pharmacy, "sooner@example.org", TestPharmacies.RegisteredAt.PlusDays(-5));
        pharmacy.Stored(store);
        var handler = Handler(store);

        var first = await handler.HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Owner, Page.First, PageSizeOf(2)), TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Owner, PageOf(2), PageSizeOf(2)), TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Items.Select(invitation => invitation.MembershipId).ShouldBe([expired.Value, sooner.Value]);
        first.Value.TotalCount.ShouldBe(3);
        first.Value.Page.ShouldBe(1);
        first.Value.PageSize.ShouldBe(2);
        second.IsSuccess.ShouldBeTrue();
        second.Value.Items.ShouldHaveSingleItem().MembershipId.ShouldBe(latest.Value);
        second.Value.TotalCount.ShouldBe(3);
        second.Value.Page.ShouldBe(2);
        second.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Each_invitation_shows_membership_email_job_function_role_and_expiry()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var at = TestPharmacies.RegisteredAt.PlusDays(-3);
        var open = TestPharmacies.Invite(pharmacy, "open@example.org", at);
        var renewed = pharmacy.Invite(MembershipId.New(), EmailAddress.From("renewed@example.org"), JobFunction.Pka, MembershipRole.Manager, at.PlusDays(-30), InvitationMailLimits.Default).Value;
        var renewedAt = TestPharmacies.RegisteredAt.PlusDays(1);
        pharmacy.RenewInvitation(renewed, renewedAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);

        var listed = await Handler(store).HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.ShouldBe([
            new PharmacyInvitationDto(
                open.Value,
                "open@example.org",
                OrganisationJobFunction.Pta,
                OrganisationMembershipRole.Employee,
                at.PlusDays(14).Value),
            new PharmacyInvitationDto(
                renewed.Value,
                "renewed@example.org",
                OrganisationJobFunction.Pka,
                OrganisationMembershipRole.Manager,
                renewedAt.PlusDays(14).Value),
        ]);
    }

    [Fact]
    public async Task Active_ended_and_revoked_memberships_are_no_invitations()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        var ben = TestPharmacies.AddMember(pharmacy, Ben, MembershipRole.Employee);
        pharmacy.EndMembership(ben, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        var revoked = TestPharmacies.Invite(pharmacy, "revoked@example.org");
        pharmacy.RevokeInvitation(revoked).IsSuccess.ShouldBeTrue();
        var invited = TestPharmacies.Invite(pharmacy, "invited@example.org");
        pharmacy.Stored(store);

        var listed = await Handler(store).HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.ShouldHaveSingleItem().MembershipId.ShouldBe(invited.Value);
        listed.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_employee_may_not_list_the_invitations()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        TestPharmacies.Invite(pharmacy, "invited@example.org");
        pharmacy.Stored(store);

        var refused = await Handler(store).HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Anna, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

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
            new ListPharmacyInvitations(pharmacy.Id, PersonId.From(Guid.CreateVersion7()), Page.First, PageSize.Default), TestContext.Current.CancellationToken);
        var ended = await handler.HandleAsync(
            new ListPharmacyInvitations(pharmacy.Id, Ben, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

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
            new ListPharmacyInvitations(PharmacyId.New(), Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static ListPharmacyInvitationsHandler Handler(InMemoryPharmacyStore store) => new(store);

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
