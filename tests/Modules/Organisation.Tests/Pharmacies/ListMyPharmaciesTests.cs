using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListMyPharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class ListMyPharmaciesTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress AnnaMail = EmailAddress.From("anna@example.org");

    [Fact]
    public async Task My_pharmacies_are_listed_by_name_a_page_at_a_time_with_how_many_there_are()
    {
        var store = new InMemoryPharmacyStore();
        foreach (var name in new[] { "Cleo-Apotheke", "Adler-Apotheke", "Bären-Apotheke" })
        {
            store.Stored.Add(Register(name, Owner));
        }

        store.Stored.Add(Register("Andere Apotheke", PersonId.From(Guid.CreateVersion7())));

        var page = await new ListMyPharmaciesHandler(store)
            .HandleAsync(new ListMyPharmacies(Owner, PageOf(2), PageSizeOf(2)), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Cleo-Apotheke");
        page.Value.TotalCount.ShouldBe(3);
        page.Value.Page.ShouldBe(2);
        page.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Only_pharmacies_where_I_am_an_active_member_are_mine()
    {
        var store = new InMemoryPharmacyStore();
        var invitedOnly = Register("Adler-Apotheke", Owner);
        Invite(invitedOnly);
        var left = Register("Bären-Apotheke", Owner);
        left.EndMembership(Join(left), Now).IsSuccess.ShouldBeTrue();
        var active = Register("Cleo-Apotheke", Owner);
        Join(active);
        store.Stored.AddRange([invitedOnly, left, active]);

        var page = await new ListMyPharmaciesHandler(store)
            .HandleAsync(new ListMyPharmacies(Anna, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(active.Id.Value);
        page.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Each_pharmacy_shows_my_membership_with_its_role_job_function_and_what_I_may_do()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = Register("Stadt-Apotheke", Owner);
        var annasMembership = Join(pharmacy);
        store.Stored.Add(pharmacy);
        var handler = new ListMyPharmaciesHandler(store);

        var owners = await handler.HandleAsync(new ListMyPharmacies(Owner, Page.First, PageSize.Default), TestContext.Current.CancellationToken);
        var annas = await handler.HandleAsync(new ListMyPharmacies(Anna, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        owners.Value.Items.ShouldHaveSingleItem().ShouldBe(new MyPharmacyDto(
            pharmacy.Id.Value,
            "Stadt-Apotheke",
            pharmacy.ActiveMembershipOf(Owner).Value.Id.Value,
            OrganisationMembershipRole.Manager,
            OrganisationJobFunction.Pharmacist,
            CanManageMembers: true,
            CanAssignCourses: true));
        annas.Value.Items.ShouldHaveSingleItem().ShouldBe(new MyPharmacyDto(
            pharmacy.Id.Value,
            "Stadt-Apotheke",
            annasMembership.Value,
            OrganisationMembershipRole.Employee,
            OrganisationJobFunction.Pta,
            CanManageMembers: false,
            CanAssignCourses: false));
    }

    [Fact]
    public async Task Without_a_membership_the_first_page_is_empty()
    {
        var page = await new ListMyPharmaciesHandler(new InMemoryPharmacyStore())
            .HandleAsync(new ListMyPharmacies(Anna, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldBeEmpty();
        page.Value.TotalCount.ShouldBe(0);
    }

    private static Pharmacy Register(string name, PersonId founder) =>
        Pharmacy.Register(
            PharmacyId.New(),
            PharmacyName.From(name),
            MembershipId.New(),
            founder,
            DisplayName.From("Olaf Owner"),
            EmailAddress.From("owner@example.com"),
            JobFunction.Pharmacist,
            Now);

    private static InvitationToken Invite(Pharmacy pharmacy)
    {
        var token = InvitationToken.From(RandomNumberGenerator.GetHexString(64));
        var membership = MembershipId.New();
        pharmacy.Invite(membership, AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, token, Now).IsSuccess.ShouldBeTrue();
        return token;
    }

    private static MembershipId Join(Pharmacy pharmacy)
    {
        var token = Invite(pharmacy);
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).IsSuccess.ShouldBeTrue();
        return pharmacy.ActiveMembershipOf(Anna).Value.Id;
    }

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
