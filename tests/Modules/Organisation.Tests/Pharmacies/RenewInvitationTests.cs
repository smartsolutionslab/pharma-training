using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager sends a fresh link: the old one stops working, and the invitation runs for another 14 days.</summary>
public class RenewInvitationTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Invitee = EmailAddress.From("invitee@example.org");

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public async Task A_manager_renews_an_invitation_open_or_run_out_with_a_new_token_valid_for_14_days(int daysOld)
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var old = Token();
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, old, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start)).Value;
        pharmacy.Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromDays(daysOld) + TimeSpan.FromMinutes(5));
        var now = Start.AddDays(daysOld).AddMinutes(5);

        var renewed = await new RenewInvitationHandler(store, store, time)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        var raised = pharmacy.DomainEvents.OfType<MemberInvited>().SingleOrDefault();
        raised.ShouldNotBeNull("renewing raised no MemberInvited");
        raised.Membership.ShouldBe(invitation);
        raised.Email.ShouldBe(Invitee);
        raised.Token.ShouldNotBe(old);
        raised.ExpiresAt.ShouldBe(Timestamp.From(now.AddDays(14)));
        var membership = pharmacy.Membership(invitation);
        membership.Status.ShouldBe(MembershipStatus.Invited);
        membership.TokenHash.ShouldBe(InvitationTokenHash.Of(raised.Token));
        membership.TokenHash.ShouldNotBe(InvitationTokenHash.Of(old));
        membership.InvitationExpiresAt.ShouldBe(Timestamp.From(now.AddDays(14)));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_renewal_requests_its_mail_in_its_own_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);

        var renewed = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        var requested = store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>();
        requested.MembershipId.ShouldBe(invitation.Value);
        requested.PharmacyId.ShouldBe(pharmacy.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_renewal_is_stored_without_a_link_until_its_mail_issues_one()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);

        var renewed = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(invitation).TokenHash.ShouldBeNull();
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
            var refused = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, membership, Owner), TestContext.Current.CancellationToken);

            refused.IsSuccess.ShouldBeFalse($"{membership} was renewed");
            refused.Error.Code.ShouldBe("organisation.no-open-invitation");
            refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        }

        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_employee_may_not_renew_and_a_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        var before = pharmacy.Membership(invitation).InvitationExpiresAt;
        var handler = Handler(store);

        var employee = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Anna), TestContext.Current.CancellationToken);
        var stranger = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Ben), TestContext.Current.CancellationToken);

        employee.IsSuccess.ShouldBeFalse("an employee renewed an invitation");
        employee.Error.Code.ShouldBe("organisation.capability-missing");
        employee.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        stranger.IsSuccess.ShouldBeFalse("a stranger renewed an invitation");
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(invitation).InvitationExpiresAt.ShouldBe(before);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_renewal_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    private static RenewInvitationHandler Handler(InMemoryPharmacyStore store) => new(store, store, new FakeTimeProvider(Start));

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
