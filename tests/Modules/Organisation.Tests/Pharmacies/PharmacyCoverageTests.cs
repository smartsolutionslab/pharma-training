using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>The branches of the Pharmacy aggregate the reference tests do not reach.</summary>
public class PharmacyCoverageTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress AnnaMail = EmailAddress.From("anna@example.org");
    private static readonly DisplayName AnnaName = DisplayName.From("Anna Berger");

    private static readonly string[] DomainErrorCodes =
    [
        "organisation.already-member-or-invited",
        "organisation.no-open-invitation",
        "organisation.invitation-not-found",
        "organisation.invitation-expired",
        "organisation.invitation-for-another-address",
        "organisation.person-already-member",
        "organisation.no-active-membership",
        "organisation.last-manager",
    ];

    [Fact]
    public void Registering_raises_that_the_pharmacy_was_registered_and_that_the_founder_started_now()
    {
        var id = PharmacyId.New();
        var founderMembership = MembershipId.New();
        var name = PharmacyName.From("Stadt-Apotheke");

        var pharmacy = Pharmacy.Register(id, name, founderMembership, Owner, DisplayName.From("Olaf Owner"), EmailAddress.From("owner@example.org"), JobFunction.Pta, Now);

        pharmacy.Id.ShouldBe(id);
        pharmacy.Name.ShouldBe(name);
        pharmacy.RegisteredAt.ShouldBe(Now);
        pharmacy.DomainEvents.OfType<PharmacyRegistered>().ShouldHaveSingleItem().ShouldBe(new PharmacyRegistered(id, name));
        pharmacy.DomainEvents.OfType<MembershipStarted>().ShouldHaveSingleItem().ShouldBe(
            new MembershipStarted(id, founderMembership, Owner, DisplayName.From("Olaf Owner"), JobFunction.Pta, MembershipRole.Manager, Now));
        var founder = pharmacy.Memberships.ShouldHaveSingleItem();
        founder.Id.ShouldBe(founderMembership);
        founder.Person.ShouldBe(Owner);
        founder.Email.ShouldBe(EmailAddress.From("owner@example.org"));
        founder.JobFunction.ShouldBe(JobFunction.Pta);
        founder.StartedAt.ShouldBe(Now);
    }

    [Fact]
    public void Inviting_raises_that_the_member_was_invited_until_the_link_runs_out()
    {
        var pharmacy = Register();
        var token = Token();

        var membership = Invite(pharmacy, AnnaMail, token);

        pharmacy.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem()
            .ShouldBe(new MemberInvited(pharmacy.Id, membership, Now.PlusDays(Pharmacy.InvitationValidityDays)));
        var invited = pharmacy.Memberships.Single(m => m.Id == membership);
        invited.Status.ShouldBe(MembershipStatus.Invited);
        invited.Person.ShouldBeNull();
        invited.InvitationExpiresAt.ShouldBe(Now.PlusDays(Pharmacy.InvitationValidityDays));
    }

    [Fact]
    public void Accepting_raises_that_the_membership_started_at_the_moment_of_accepting()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);

        pharmacy.AcceptInvitation(token, Anna, AnnaName, AnnaMail, Now.PlusDays(3));

        pharmacy.DomainEvents.OfType<MembershipStarted>().Last()
            .ShouldBe(new MembershipStarted(pharmacy.Id, membership, Anna, AnnaName, JobFunction.Pta, MembershipRole.Employee, Now.PlusDays(3)));
    }

    [Fact]
    public void Revoking_an_open_invitation_removes_it_and_burns_its_link()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);

        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();

        var revoked = pharmacy.Memberships.Single(m => m.Id == membership);
        revoked.Status.ShouldBe(MembershipStatus.Revoked);
        revoked.TokenHash.ShouldBeNull();
        pharmacy.DomainEvents.OfType<InvitationRevoked>().ShouldHaveSingleItem().ShouldBe(new InvitationRevoked(pharmacy.Id, membership));
        pharmacy.AcceptInvitation(token, Anna, AnnaName, AnnaMail, Now).Error.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void Revoking_an_invitation_twice_finds_no_open_invitation()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        pharmacy.RevokeInvitation(membership);

        pharmacy.RevokeInvitation(membership).Error.ShouldBeOfType<NoOpenInvitation>();
    }

    [Fact]
    public void A_revoked_address_can_be_invited_again()
    {
        var pharmacy = Register();
        pharmacy.RevokeInvitation(Invite(pharmacy, AnnaMail));

        pharmacy.Invite(MembershipId.New(), AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Renewing_a_membership_that_is_no_open_invitation_is_refused()
    {
        var pharmacy = Register();
        var founder = pharmacy.Memberships.Single().Id;

        pharmacy.RenewInvitation(founder, Now).Error.ShouldBeOfType<NoOpenInvitation>();
        pharmacy.RenewInvitation(MembershipId.New(), Now).Error.ShouldBeOfType<NoOpenInvitation>();
    }

    [Fact]
    public void Renewing_raises_that_the_member_was_invited_again_with_the_new_link()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);

        pharmacy.RenewInvitation(membership, Now.PlusDays(20)).IsSuccess.ShouldBeTrue();

        pharmacy.DomainEvents.OfType<MemberInvited>().Last()
            .ShouldBe(new MemberInvited(pharmacy.Id, membership, Now.PlusDays(20 + Pharmacy.InvitationValidityDays)));
    }

    [Fact]
    public void A_person_already_active_in_the_pharmacy_cannot_accept_a_second_invitation()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);
        pharmacy.AcceptInvitation(token, Anna, AnnaName, AnnaMail, Now);
        var second = Token();
        var privateMail = EmailAddress.From("anna.private@example.org");
        Invite(pharmacy, privateMail, second);

        pharmacy.AcceptInvitation(second, Anna, AnnaName, privateMail, Now).Error.ShouldBeOfType<PersonAlreadyMember>();
    }

    [Fact]
    public void Ending_a_membership_that_is_unknown_or_only_invited_is_refused()
    {
        var pharmacy = Register();
        var invited = Invite(pharmacy, AnnaMail);

        pharmacy.EndMembership(MembershipId.New(), Now).Error.ShouldBeOfType<NoActiveMembership>();
        pharmacy.EndMembership(invited, Now).Error.ShouldBeOfType<NoActiveMembership>();
    }

    [Fact]
    public void Ending_an_employees_membership_records_when_it_ended()
    {
        var pharmacy = Register();
        var anna = Join(pharmacy);

        pharmacy.EndMembership(anna, Now.PlusDays(5)).IsSuccess.ShouldBeTrue();

        var ended = pharmacy.Memberships.Single(m => m.Id == anna);
        ended.Status.ShouldBe(MembershipStatus.Ended);
        ended.EndedAt.ShouldBe(Now.PlusDays(5));
        pharmacy.DomainEvents.OfType<MembershipEnded>().ShouldHaveSingleItem().ShouldBe(new MembershipEnded(pharmacy.Id, anna, Anna, Now.PlusDays(5)));
    }

    [Fact]
    public void Changing_a_membership_raises_that_it_changed()
    {
        var pharmacy = Register();
        var anna = Join(pharmacy);

        pharmacy.ChangeMembership(anna, JobFunction.Pka, MembershipRole.Manager).IsSuccess.ShouldBeTrue();

        var changed = pharmacy.Memberships.Single(m => m.Id == anna);
        changed.JobFunction.ShouldBe(JobFunction.Pka);
        changed.Role.ShouldBe(MembershipRole.Manager);
        pharmacy.DomainEvents.OfType<MembershipChanged>().ShouldHaveSingleItem()
            .ShouldBe(new MembershipChanged(pharmacy.Id, anna, Anna, JobFunction.Pka, MembershipRole.Manager));
    }

    [Fact]
    public void Changing_a_membership_to_what_it_already_is_raises_nothing()
    {
        var pharmacy = Register();
        var anna = Join(pharmacy);

        pharmacy.ChangeMembership(anna, JobFunction.Pta, MembershipRole.Employee).IsSuccess.ShouldBeTrue();

        pharmacy.DomainEvents.OfType<MembershipChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void Changing_an_unknown_membership_is_refused()
    {
        var pharmacy = Register();

        pharmacy.ChangeMembership(MembershipId.New(), JobFunction.Pta, MembershipRole.Employee).Error.ShouldBeOfType<NoActiveMembership>();
    }

    [Fact]
    public void A_manager_may_step_down_once_there_is_a_second_one()
    {
        var pharmacy = Register();
        var founder = pharmacy.Memberships.Single().Id;
        Join(pharmacy, MembershipRole.Manager);

        pharmacy.ChangeMembership(founder, JobFunction.Pharmacist, MembershipRole.Employee).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_founder_has_an_active_membership_and_a_stranger_none()
    {
        var pharmacy = Register();

        pharmacy.ActiveMembershipOf(Owner).Value.ShouldBe(pharmacy.Memberships.Single());
        pharmacy.ActiveMembershipOf(PersonId.From(Guid.CreateVersion7())).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Only_a_manager_may_manage_members()
    {
        MembershipRole.Manager.Grants(Capability.ManageMembers).ShouldBeTrue();
        MembershipRole.Employee.Grants(Capability.ManageMembers).ShouldBeFalse();
    }

    [Fact]
    public void Each_domain_error_names_its_own_code()
    {
        var pharmacy = PharmacyId.New();
        var membership = MembershipId.New();
        List<DomainError> errors =
        [
            new AlreadyMemberOrInvited(pharmacy, AnnaMail),
            new NoOpenInvitation(pharmacy, membership),
            new InvitationNotFound(pharmacy),
            new InvitationExpired(pharmacy, membership),
            new InvitationForAnotherAddress(pharmacy, membership),
            new PersonAlreadyMember(pharmacy, Anna),
            new NoActiveMembership(pharmacy, membership),
            new LastManagerCannotLeave(pharmacy),
        ];

        errors.Select(error => error.Code).ShouldBe(DomainErrorCodes);
        errors.ShouldAllBe(error => !string.IsNullOrWhiteSpace(error.Message));
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

    private static MembershipId Join(Pharmacy pharmacy, MembershipRole role = MembershipRole.Employee)
    {
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token, role);
        pharmacy.AcceptInvitation(token, Anna, AnnaName, AnnaMail, Now).IsSuccess.ShouldBeTrue();
        return membership;
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
