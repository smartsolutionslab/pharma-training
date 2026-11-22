using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>The reference tests of the Pharmacy aggregate (docs/domain/reference/tests/PharmacyTests.cs).</summary>
public class PharmacyTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress AnnaMail = EmailAddress.From("anna@example.org");

    [Fact]
    public void The_founder_is_the_first_active_manager()
    {
        var pharmacy = Register();

        var founder = pharmacy.Memberships.ShouldHaveSingleItem();
        founder.Status.ShouldBe(MembershipStatus.Active);
        founder.Role.ShouldBe(MembershipRole.Manager);
        pharmacy.DomainEvents.OfType<MembershipStarted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_address_is_invited_only_once()
    {
        var pharmacy = Register();
        Invite(pharmacy, AnnaMail);

        pharmacy.Invite(MembershipId.New(), EmailAddress.From("ANNA@example.org"), Token(), JobFunction.Pta, MembershipRole.Employee, Now)
            .Error.ShouldBeOfType<AlreadyMemberOrInvited>();
    }

    [Fact]
    public void Accepting_activates_the_membership_with_the_name_from_keycloak_and_burns_the_link()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);
        var anna = pharmacy.Memberships.Single(m => m.Id == membership);
        anna.TokenHash.ShouldBe(InvitationTokenHash.Of(token));

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(2)).IsSuccess.ShouldBeTrue();

        anna.Status.ShouldBe(MembershipStatus.Active);
        anna.Name.ShouldBe(DisplayName.From("Anna Berger"));
        anna.TokenHash.ShouldBeNull();
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(2))
            .Error.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void An_invitation_keeps_only_the_hash_of_its_token()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);

        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(token));
    }

    [Fact]
    public void The_invitation_carries_the_pharmacys_name_and_the_token_for_the_mail()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);

        var invited = pharmacy.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem();
        invited.Name.ShouldBe(PharmacyName.From("Stadt-Apotheke Schwarzenfeld"));
        invited.Token.ShouldBe(token);
    }

    [Fact]
    public void Neither_the_token_nor_the_invitation_prints_the_secret()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);

        token.ToString().ShouldNotContain(token.Value);
        pharmacy.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem().ToString().ShouldNotContain(token.Value);
    }

    [Fact]
    public void An_invitation_runs_out_after_14_days()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(15))
            .Error.ShouldBeOfType<InvitationExpired>();
    }

    [Fact]
    public void A_renewed_invitation_works_with_the_new_link_only()
    {
        var pharmacy = Register();
        var old = Token();
        var membership = Invite(pharmacy, AnnaMail, old);
        var fresh = Token();

        pharmacy.RenewInvitation(membership, fresh, Now.PlusDays(20));

        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(fresh));
        pharmacy.DomainEvents.OfType<MemberInvited>().Last().Name.ShouldBe(PharmacyName.From("Stadt-Apotheke Schwarzenfeld"));
        pharmacy.AcceptInvitation(old, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(21)).Error.ShouldBeOfType<InvitationNotFound>();
        pharmacy.AcceptInvitation(fresh, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(21)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_revoked_invitation_keeps_no_hash_and_its_link_is_refused()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);
        var revoked = pharmacy.Memberships.Single(m => m.Id == membership);
        revoked.TokenHash.ShouldBe(InvitationTokenHash.Of(token));

        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();

        revoked.TokenHash.ShouldBeNull();
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).Error.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void A_link_forwarded_to_someone_else_is_refused()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Bert Klein"), EmailAddress.From("bert@example.org"), Now)
            .Error.ShouldBeOfType<InvitationForAnotherAddress>();
    }

    [Fact]
    public void The_last_manager_can_neither_leave_nor_step_down()
    {
        var pharmacy = Register();
        var founder = pharmacy.Memberships.Single().Id;

        pharmacy.EndMembership(founder, Now).Error.ShouldBeOfType<LastManagerCannotLeave>();
        pharmacy.ChangeMembership(founder, JobFunction.Pharmacist, MembershipRole.Employee).Error.ShouldBeOfType<LastManagerCannotLeave>();
    }

    [Fact]
    public void A_manager_may_leave_once_there_is_a_second_one()
    {
        var pharmacy = Register();
        var founder = pharmacy.Memberships.Single().Id;
        var token = Token();
        Invite(pharmacy, AnnaMail, token, MembershipRole.Manager);
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now);

        pharmacy.EndMembership(founder, Now).IsSuccess.ShouldBeTrue();

        pharmacy.DomainEvents.OfType<MembershipEnded>().ShouldHaveSingleItem().Person.ShouldBe(Owner);
    }

    [Fact]
    public void A_person_who_left_can_be_invited_again()
    {
        var pharmacy = Register();
        var token = Token();
        var first = Invite(pharmacy, AnnaMail, token);
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now);
        pharmacy.EndMembership(first, Now);

        pharmacy.Invite(MembershipId.New(), AnnaMail, Token(), JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Issuing_a_link_stores_the_hash_of_its_token_and_answers_the_expiry()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        pharmacy.ClearDomainEvents();
        var token = Token();

        var issued = pharmacy.IssueInvitationLink(membership, token, Later(1));

        issued.IsSuccess.ShouldBeTrue();
        issued.Value.ShouldBe(Now.PlusDays(14));
        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(token));
        var raised = pharmacy.DomainEvents.OfType<InvitationLinkIssued>().SingleOrDefault();
        raised.ShouldNotBeNull("no InvitationLinkIssued raised");
        raised.Pharmacy.ShouldBe(pharmacy.Id);
        raised.Membership.ShouldBe(membership);
    }

    [Fact]
    public void A_link_issued_again_replaces_the_one_before()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        var first = Token();
        var second = Token();

        pharmacy.IssueInvitationLink(membership, first, Later(1)).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, second, Later(2)).IsSuccess.ShouldBeTrue();

        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(second));
        pharmacy.AcceptInvitation(first, Anna, DisplayName.From("Anna Berger"), AnnaMail, Later(3)).Error.ShouldBeOfType<InvitationNotFound>();
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("accepted")]
    [InlineData("founder")]
    [InlineData("unknown")]
    public void A_link_is_issued_only_for_an_open_invitation(string which)
    {
        var pharmacy = Register();
        var token = Token();
        var invited = Invite(pharmacy, AnnaMail, token);
        var membership = which switch
        {
            "revoked" => Revoked(pharmacy, invited),
            "accepted" => Accepted(pharmacy, invited, token),
            "founder" => pharmacy.Memberships[0].Id,
            _ => MembershipId.New(),
        };
        List<InvitationTokenHash?> before = [.. pharmacy.Memberships.Select(m => m.TokenHash)];
        pharmacy.ClearDomainEvents();

        var issued = pharmacy.IssueInvitationLink(membership, Token(), Later(1));

        issued.IsSuccess.ShouldBeFalse($"a link was issued for the {which} membership: should be NoOpenInvitation but was SuccessOutcome");
        issued.Error.ShouldBeOfType<NoOpenInvitation>();
        pharmacy.Memberships.Select(m => m.TokenHash).ShouldBe(before);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void No_link_is_issued_for_an_invitation_that_has_run_out()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);
        var expiresAt = Now.PlusDays(14);
        pharmacy.ClearDomainEvents();

        var issued = pharmacy.IssueInvitationLink(membership, Token(), Timestamp.From(expiresAt.Value.AddTicks(1)));

        issued.IsSuccess.ShouldBeFalse("a link was issued for an invitation that had run out: should be InvitationExpired but was SuccessOutcome");
        issued.Error.ShouldBeOfType<InvitationExpired>();
        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(token));
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void InvitationLinkIssued_carries_no_token()
    {
        typeof(InvitationLinkIssued).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["Membership", "Pharmacy"]);
    }

    [Fact]
    public void Only_a_manager_may_assign_courses()
    {
        MembershipRole.Manager.Grants(Capability.AssignCourses).ShouldBeTrue();
        MembershipRole.Employee.Grants(Capability.AssignCourses).ShouldBeFalse();
    }

    private static Pharmacy Register() =>
        Pharmacy.Register(
            PharmacyId.New(),
            PharmacyName.From("Stadt-Apotheke Schwarzenfeld"),
            MembershipId.New(),
            Owner,
            DisplayName.From("Inhaberin"),
            EmailAddress.From("chefin@example.org"),
            JobFunction.Pharmacist,
            Now);

    private static MembershipId Invite(Pharmacy pharmacy, EmailAddress email, InvitationToken? token = null, MembershipRole role = MembershipRole.Employee) =>
        pharmacy.Invite(MembershipId.New(), email, token ?? Token(), JobFunction.Pta, role, Now).Value;

    private static Timestamp Later(int minutes) => Timestamp.From(Now.Value.AddMinutes(minutes));

    private static MembershipId Revoked(Pharmacy pharmacy, MembershipId membership)
    {
        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        return membership;
    }

    private static MembershipId Accepted(Pharmacy pharmacy, MembershipId membership, InvitationToken token)
    {
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).IsSuccess.ShouldBeTrue();
        return membership;
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
