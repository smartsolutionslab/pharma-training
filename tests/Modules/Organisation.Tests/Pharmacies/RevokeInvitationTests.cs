using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager withdraws an open invitation: its link stops working, and the address may be invited again.</summary>
public class RevokeInvitationTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_revokes_an_open_invitation_and_the_address_is_free_again()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);

        var revoked = await Handler(store).HandleAsync(new RevokeInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        revoked.IsSuccess.ShouldBeTrue();
        var membership = pharmacy.Membership(invitation);
        membership.Status.ShouldBe(MembershipStatus.Revoked);
        membership.TokenHash.ShouldBeNull();
        pharmacy.DomainEvents.OfType<InvitationRevoked>().ShouldHaveSingleItem().ShouldBe(new InvitationRevoked(pharmacy.Id, invitation));
        store.Commits.ShouldBe(1);
        TestPharmacies.Invite(pharmacy, "invitee@example.org");
    }

    [Fact]
    public async Task A_membership_that_is_no_open_invitation_of_this_pharmacy_is_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var revoked = TestPharmacies.Invite(pharmacy, "revoked@example.org");
        pharmacy.RevokeInvitation(revoked).IsSuccess.ShouldBeTrue();
        var ended = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.EndMembership(ended, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var elsewhere = TestPharmacies.Invite(TestPharmacies.Register(Owner, "Markt-Apotheke").Stored(store), "elsewhere@example.org");
        var handler = Handler(store);
        List<MembershipId> notOpenHere = [TestPharmacies.FounderOf(pharmacy), ended, revoked, MembershipId.New(), elsewhere];

        foreach (var membership in notOpenHere)
        {
            var refused = await handler.HandleAsync(new RevokeInvitation(pharmacy.Id, membership, Owner), TestContext.Current.CancellationToken);

            refused.IsSuccess.ShouldBeFalse($"{membership} was revoked");
            refused.Error.Code.ShouldBe("organisation.no-open-invitation");
            refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        }

        pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)).Status.ShouldBe(MembershipStatus.Active);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_employee_may_not_revoke_and_a_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        var handler = Handler(store);

        var employee = await handler.HandleAsync(new RevokeInvitation(pharmacy.Id, invitation, Anna), TestContext.Current.CancellationToken);
        var stranger = await handler.HandleAsync(new RevokeInvitation(pharmacy.Id, invitation, Ben), TestContext.Current.CancellationToken);

        employee.IsSuccess.ShouldBeFalse("an employee revoked an invitation");
        employee.Error.Code.ShouldBe("organisation.capability-missing");
        employee.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        stranger.IsSuccess.ShouldBeFalse("a stranger revoked an invitation");
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Invited);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_revocation_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new RevokeInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
    }

    private static RevokeInvitationHandler Handler(InMemoryPharmacyStore store) => new(store, store);
}
