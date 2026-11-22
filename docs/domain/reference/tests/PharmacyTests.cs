// Organisation — behaviour of the Pharmacy aggregate and its memberships.

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Tests.Pharmacies;

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

        pharmacy.Invite(MembershipId.New(), EmailAddress.From("ANNA@example.org"), JobFunction.Pta, MembershipRole.Employee, Now)
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
    public void An_invitation_holds_no_link_until_its_mail_issues_one()
    {
        var pharmacy = Register();
        var membership = MembershipId.New();

        pharmacy.Invite(membership, AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();

        var invited = pharmacy.Memberships.Single(m => m.Id == membership);
        invited.TokenHash.ShouldBeNull();
        invited.InvitationExpiresAt.ShouldBe(Now.PlusDays(14));
    }

    [Fact]
    public void Issuing_a_link_stores_the_hash_of_its_token_and_answers_the_expiry()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        var token = Token();

        pharmacy.IssueInvitationLink(membership, token, Now).Value.ShouldBe(Now.PlusDays(14));

        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(token));
        pharmacy.DomainEvents.OfType<InvitationLinkIssued>().ShouldHaveSingleItem();
    }

    [Fact]
    public void No_link_is_issued_for_an_invitation_that_has_run_out()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);

        pharmacy.IssueInvitationLink(membership, Token(), Now.PlusDays(15)).Error.ShouldBeOfType<InvitationExpired>();
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

        pharmacy.RenewInvitation(membership, Now.PlusDays(20));
        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBeNull();
        pharmacy.IssueInvitationLink(membership, fresh, Now.PlusDays(20)).IsSuccess.ShouldBeTrue();

        pharmacy.Memberships.Single(m => m.Id == membership).TokenHash.ShouldBe(InvitationTokenHash.Of(fresh));
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

        pharmacy.Invite(MembershipId.New(), AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
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

    /// <summary>Invites the address; with a token, its mail has issued that link too.</summary>
    private static MembershipId Invite(Pharmacy pharmacy, EmailAddress email, InvitationToken? token = null, MembershipRole role = MembershipRole.Employee)
    {
        var membership = pharmacy.Invite(MembershipId.New(), email, JobFunction.Pta, role, Now).Value;
        if (token is not null)
        {
            pharmacy.IssueInvitationLink(membership, token, Now).IsSuccess.ShouldBeTrue();
        }

        return membership;
    }

    private static InvitationToken Token() => InvitationToken.From(Convert.ToHexString(Guid.NewGuid().ToByteArray()));
}
