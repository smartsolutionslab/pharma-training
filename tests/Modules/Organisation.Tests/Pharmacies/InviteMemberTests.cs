using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager invites an address; the invitation keeps only its token's hash and is refused without a manager's say.</summary>
public class InviteMemberTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Invitee = EmailAddress.From("invitee@example.org");

    [Fact]
    public async Task A_manager_invites_an_address_and_the_invitation_is_committed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(5));

        var invited = await new InviteMemberHandler(store, store, time)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pka, MembershipRole.Manager), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var membership = pharmacy.Memberships.SingleOrDefault(candidate => candidate.Id.Value == invited.Value);
        membership.ShouldNotBeNull($"no membership {invited.Value} was stored");
        membership.Status.ShouldBe(MembershipStatus.Invited);
        membership.Email.ShouldBe(Invitee);
        membership.JobFunction.ShouldBe(JobFunction.Pka);
        membership.Role.ShouldBe(MembershipRole.Manager);
        membership.InvitationExpiresAt.ShouldBe(Timestamp.From(Start.AddMinutes(5).AddDays(14)));
        var raised = pharmacy.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem();
        raised.Membership.ShouldBe(membership.Id);
        raised.ExpiresAt.ShouldBe(Timestamp.From(Start.AddMinutes(5).AddDays(14)));
        membership.InvitationLinks.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_invitation_requests_its_mail_in_its_own_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var requested = store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>();
        requested.MembershipId.ShouldBe(invited.Value);
        requested.PharmacyId.ShouldBe(pharmacy.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_request_that_goes_away_as_its_commit_completes_still_has_its_mail_requested()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        using var request = new CancellationTokenSource();
        store.OnCommit(request.Cancel);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), request.Token);

        request.IsCancellationRequested.ShouldBeTrue();
        invited.IsSuccess.ShouldBeTrue();
        store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>().MembershipId.ShouldBe(invited.Value);
    }

    [Fact]
    public async Task An_invitation_is_stored_without_a_link_until_its_mail_issues_one()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(MembershipId.From(invited.Value)).InvitationLinks.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_employee_may_not_invite()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);

        var refused = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Anna, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("an employee's invitation was accepted");
        refused.Error.Code.ShouldBe("organisation.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        pharmacy.Memberships.ShouldNotContain(membership => membership.Email == Invitee);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_stranger_and_an_unknown_pharmacy_are_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var handler = Handler(store);

        var stranger = await handler.HandleAsync(new InviteMember(pharmacy.Id, Ben, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var unknown = await handler.HandleAsync(new InviteMember(PharmacyId.New(), Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        stranger.IsSuccess.ShouldBeFalse("a stranger's invitation was accepted");
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.IsSuccess.ShouldBeFalse("an invitation to an unknown pharmacy was accepted");
        unknown.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Memberships.ShouldNotContain(membership => membership.Email == Invitee);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_address_that_is_invited_or_active_is_refused_as_a_conflict()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.Invite(pharmacy, "invited@example.org");
        pharmacy.Stored(store);
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)).Email;
        var handler = Handler(store);
        List<EmailAddress> taken = [EmailAddress.From("Invited@Example.org"), founder];

        foreach (var address in taken)
        {
            var refused = await handler.HandleAsync(new InviteMember(pharmacy.Id, Owner, address, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

            refused.IsSuccess.ShouldBeFalse($"{address} was invited again");
            refused.Error.Code.ShouldBe("organisation.already-member-or-invited");
            refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        }

        pharmacy.Memberships.Count.ShouldBe(2);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_invitation_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    private static InviteMemberHandler Handler(InMemoryPharmacyStore store) => new(store, store, new FakeTimeProvider(Start));
}
