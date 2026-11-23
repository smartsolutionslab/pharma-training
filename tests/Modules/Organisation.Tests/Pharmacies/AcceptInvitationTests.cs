using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The invited person accepts with the token from the link, as Keycloak knows them: the membership starts, the link
/// dies, and every refusal answers with its own code and status.
/// </summary>
public class AcceptInvitationTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Bert = PersonId.From(Guid.CreateVersion7());
    private static readonly DisplayName AnnaName = DisplayName.From("Anna Berger");
    private static readonly EmailAddress AnnaMail = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value);
    private readonly Pharmacy pharmacy;
    private readonly MembershipId invitation;
    private readonly InvitationToken token = Token();

    public AcceptInvitationTests()
    {
        pharmacy = TestPharmacies.Register(Owner);
        invitation = Invite(pharmacy, AnnaMail, token);
        pharmacy.Stored(store);
    }

    [Fact]
    public async Task The_invited_person_accepts_and_is_an_active_member_with_the_name_from_keycloak()
    {
        time.Advance(TimeSpan.FromHours(3));
        var now = time.GetUtcNow();

        var accepted = await AcceptAsync(token, Anna, AnnaMail);

        accepted.IsSuccess.ShouldBeTrue();
        accepted.Value.ShouldBe(pharmacy.Id.Value);
        var membership = pharmacy.Membership(invitation);
        membership.Status.ShouldBe(MembershipStatus.Active);
        membership.Person.ShouldBe(Anna);
        membership.Name.ShouldBe(AnnaName);
        membership.StartedAt.ShouldBe(Timestamp.From(now));
        membership.InvitationLinks.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
        var started = store.Published.ShouldHaveSingleItem().ShouldBeOfType<MembershipStartedV1>();
        started.MembershipId.ShouldBe(invitation.Value);
        started.PersonId.ShouldBe(Anna.Value);
        started.PharmacyId.ShouldBe(pharmacy.Id.Value);
        started.DisplayName.ShouldBe("Anna Berger");
        started.JobFunction.ShouldBe(JobFunctionV1.Pta);
        started.Role.ShouldBe(MembershipRoleV1.Employee);
        started.StartedAt.ShouldBe(now);
    }

    [Fact]
    public async Task A_token_no_pharmacy_holds_is_not_found_and_nothing_is_saved_or_announced()
    {
        var accepted = await AcceptAsync(Token(), Anna, AnnaMail);

        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.ShouldBe(PharmacyFailures.InvitationLinkNotFound());
        ShouldBeNotFound(accepted);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Invited);
    }

    [Fact]
    public async Task A_link_no_pharmacy_holds_answers_exactly_what_the_domains_invitation_not_found_maps_to()
    {
        var accepted = await AcceptAsync(Token(), Anna, AnnaMail);

        var mapped = PharmacyFailures.From(PharmacyErrors.InvitationNotFound(pharmacy.Id));
        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.Code.ShouldBe(mapped.Code);
        accepted.Error.Message.ShouldBe(mapped.Message);
        accepted.Error.Status.ShouldBe(mapped.Status);
    }

    [Fact]
    public async Task An_address_keycloak_has_not_verified_is_forbidden_and_nothing_changes_or_is_announced()
    {
        var accepted = await AcceptAsync(token, Anna, AnnaMail, verified: false);

        accepted.IsFailure.ShouldBeTrue("an unverified address accepted the invitation");
        accepted.Error.ShouldBe(PharmacyFailures.EmailNotVerified());
        accepted.Error.Code.ShouldBe("organisation.email-not-verified");
        accepted.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        var open = pharmacy.Membership(invitation);
        open.Status.ShouldBe(MembershipStatus.Invited);
        open.Person.ShouldBeNull();
        open.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        pharmacy.DomainEvents.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unverified_address_is_told_so_before_the_link_is_looked_up_so_it_learns_nothing_about_the_link()
    {
        var accepted = await AcceptAsync(Token(), Anna, AnnaMail, verified: false);

        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.ShouldBe(PharmacyFailures.EmailNotVerified());
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_invited_person_with_a_verified_address_still_accepts_after_an_unverified_attempt()
    {
        (await AcceptAsync(token, Anna, AnnaMail, verified: false)).IsFailure.ShouldBeTrue();

        var accepted = await AcceptAsync(token, Anna, AnnaMail);

        accepted.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Active);
        store.Published.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("the same person")]
    [InlineData("another person")]
    public async Task A_used_link_is_not_found(string who)
    {
        (await AcceptAsync(token, Anna, AnnaMail)).IsSuccess.ShouldBeTrue();
        var again = who == "the same person" ? Anna : Bert;

        var second = await AcceptAsync(token, again, AnnaMail);

        ShouldBeNotFound(second);
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_revoked_invitations_link_is_not_found()
    {
        pharmacy.RevokeInvitation(invitation).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        var accepted = await AcceptAsync(token, Anna, AnnaMail);

        ShouldBeNotFound(accepted);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Revoked);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_renewed_invitations_old_link_is_not_found_and_its_new_link_works()
    {
        var fresh = Token();
        pharmacy.RenewInvitation(invitation, Timestamp.From(TestPharmacies.RegisteredAt.Value.AddMinutes(10)), InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(invitation, fresh, Timestamp.From(TestPharmacies.RegisteredAt.Value.AddMinutes(10))).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        var old = await AcceptAsync(token, Anna, AnnaMail);
        var renewed = await AcceptAsync(fresh, Anna, AnnaMail);

        ShouldBeNotFound(old);
        renewed.IsSuccess.ShouldBeTrue();
        renewed.Value.ShouldBe(pharmacy.Id.Value);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public async Task An_invitation_that_ran_out_is_gone_and_stays_open()
    {
        time.Advance(TimeSpan.FromDays(Pharmacy.InvitationValidityDays) + TimeSpan.FromTicks(1));

        var accepted = await AcceptAsync(token, Anna, AnnaMail);

        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.Code.ShouldBe("organisation.invitation-expired");
        accepted.Error.Status.ShouldBe(HttpStatusCode.Gone);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Invited);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitation_is_accepted_at_the_very_instant_it_runs_out()
    {
        time.Advance(TimeSpan.FromDays(Pharmacy.InvitationValidityDays));

        var accepted = await AcceptAsync(token, Anna, AnnaMail);

        accepted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_forwarded_link_is_forbidden_and_the_invited_person_can_still_accept_it()
    {
        var forwarded = await AcceptAsync(token, Bert, EmailAddress.From("bert@example.org"));

        forwarded.IsFailure.ShouldBeTrue();
        forwarded.Error.Code.ShouldBe("organisation.invitation-for-another-address");
        forwarded.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        var open = pharmacy.Membership(invitation);
        open.Status.ShouldBe(MembershipStatus.Invited);
        open.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();

        (await AcceptAsync(token, Anna, AnnaMail)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_address_in_upper_case_from_keycloak_is_the_invited_address()
    {
        var accepted = await AcceptAsync(token, Anna, EmailAddress.From("Anna@Example.org"));

        accepted.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(invitation).Person.ShouldBe(Anna);
    }

    [Fact]
    public async Task A_person_already_active_in_the_pharmacy_is_a_conflict_and_the_invitation_stays_open()
    {
        var accepted = await AcceptAsync(token, Owner, AnnaMail);

        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.Code.ShouldBe("organisation.person-already-member");
        accepted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        var open = pharmacy.Membership(invitation);
        open.Status.ShouldBe(MembershipStatus.Invited);
        open.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_person_accepts_invitations_of_two_pharmacies_and_is_active_in_both()
    {
        var second = TestPharmacies.Register(PersonId.From(Guid.CreateVersion7()), "Markt-Apotheke");
        var secondToken = Token();
        var secondInvitation = Invite(second, AnnaMail, secondToken);
        second.Stored(store);

        var first = await AcceptAsync(token, Anna, AnnaMail);
        var other = await AcceptAsync(secondToken, Anna, AnnaMail);

        first.IsSuccess.ShouldBeTrue();
        other.IsSuccess.ShouldBeTrue();
        first.Value.ShouldBe(pharmacy.Id.Value);
        other.Value.ShouldBe(second.Id.Value);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Active);
        second.Membership(secondInvitation).Status.ShouldBe(MembershipStatus.Active);
        pharmacy.Membership(invitation).Person.ShouldBe(Anna);
        second.Membership(secondInvitation).Person.ShouldBe(Anna);
        var started = store.Published.OfType<MembershipStartedV1>().ToList();
        started.Count.ShouldBe(2);
        started.Select(e => e.PharmacyId).ShouldBe([pharmacy.Id.Value, second.Id.Value], ignoreOrder: true);
        started.ShouldAllBe(e => e.PersonId == Anna.Value);
    }

    private static void ShouldBeNotFound(Result<Guid, ApiError> result)
    {
        result.IsFailure.ShouldBeTrue("the dead link was accepted");
        result.Error.Code.ShouldBe("organisation.invitation-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        result.Error.Message.ShouldBe(PharmacyFailures.InvitationLinkNotFound().Message);
    }

    private static MembershipId Invite(Pharmacy pharmacy, EmailAddress email, InvitationToken token)
    {
        var membership = pharmacy.Invite(MembershipId.New(), email, JobFunction.Pta, MembershipRole.Employee, TestPharmacies.RegisteredAt, InvitationMailLimits.Default).Value;
        pharmacy.IssueInvitationLink(membership, token, TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        return membership;
    }

    private static InvitationToken Token() => InvitationToken.From(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

    private Task<Result<Guid, ApiError>> AcceptAsync(InvitationToken presented, PersonId person, EmailAddress email, bool verified = true) =>
        new AcceptInvitationHandler(store, store, time)
            .HandleAsync(new AcceptInvitation(presented, person, AnnaName, AccountEmail.From(email, verified)), TestContext.Current.CancellationToken);
}
