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
        anna.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(2)).IsSuccess.ShouldBeTrue();

        anna.Status.ShouldBe(MembershipStatus.Active);
        anna.Name.ShouldBe(DisplayName.From("Anna Berger"));
        anna.InvitationLinks.ShouldBeEmpty();
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(2))
            .Error.Value.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void An_invitation_keeps_only_the_hash_of_its_token()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);

        pharmacy.Memberships.Single(m => m.Id == membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
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
            .Error.Value.ShouldBeOfType<InvitationExpired>();
    }

    [Fact]
    public void A_renewed_invitation_works_with_the_new_link_only()
    {
        var pharmacy = Register();
        var old = Token();
        var membership = Invite(pharmacy, AnnaMail, old);
        var fresh = Token();

        pharmacy.RenewInvitation(membership, Now.PlusDays(20));
        pharmacy.IssueInvitationLink(membership, fresh, Now.PlusDays(20)).IsSuccess.ShouldBeTrue();

        pharmacy.Memberships.Single(m => m.Id == membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(fresh)]);
        pharmacy.AcceptInvitation(old, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(21)).Error.Value.ShouldBeOfType<InvitationNotFound>();
        pharmacy.AcceptInvitation(fresh, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now.PlusDays(21)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_revoked_invitation_keeps_no_hash_and_its_link_is_refused()
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);
        var revoked = pharmacy.Memberships.Single(m => m.Id == membership);
        revoked.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);

        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();

        revoked.InvitationLinks.ShouldBeEmpty();
        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).Error.Value.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void A_link_forwarded_to_someone_else_is_refused()
    {
        var pharmacy = Register();
        var token = Token();
        Invite(pharmacy, AnnaMail, token);

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Bert Klein"), EmailAddress.From("bert@example.org"), Now)
            .Error.Value.ShouldBeOfType<InvitationForAnotherAddress>();
    }

    [Fact]
    public void An_invitation_is_accepted_up_to_the_instant_it_runs_out_and_not_one_tick_later()
    {
        var expiresAt = Now.PlusDays(Pharmacy.InvitationValidityDays);
        var late = Register();
        var lateToken = Token();
        Invite(late, AnnaMail, lateToken);
        var onTime = Register();
        var onTimeToken = Token();
        Invite(onTime, AnnaMail, onTimeToken);

        late.AcceptInvitation(lateToken, Anna, DisplayName.From("Anna Berger"), AnnaMail, Timestamp.From(expiresAt.Value.AddTicks(1)))
            .Error.Value.ShouldBeOfType<InvitationExpired>();
        onTime.AcceptInvitation(onTimeToken, Anna, DisplayName.From("Anna Berger"), AnnaMail, expiresAt).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_person_already_active_under_another_address_cannot_accept_and_the_invitation_stays_open()
    {
        var pharmacy = Register();
        var firstToken = Token();
        Accepted(pharmacy, Invite(pharmacy, AnnaMail, firstToken), firstToken);
        var token = Token();
        var privateMail = EmailAddress.From("anna.private@example.org");
        var second = Invite(pharmacy, privateMail, token);
        pharmacy.ClearDomainEvents();

        pharmacy.AcceptInvitation(token, Anna, DisplayName.From("Anna Berger"), privateMail, Now)
            .Error.Value.ShouldBeOfType<PersonAlreadyMember>();

        var open = pharmacy.Memberships.Single(m => m.Id == second);
        open.Status.ShouldBe(MembershipStatus.Invited);
        open.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_person_active_in_another_pharmacy_accepts_here_too()
    {
        var first = Register();
        var firstToken = Token();
        var firstMembership = Invite(first, AnnaMail, firstToken);
        first.AcceptInvitation(firstToken, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).IsSuccess.ShouldBeTrue();
        var second = Register();
        var secondToken = Token();
        var secondMembership = Invite(second, AnnaMail, secondToken);

        second.AcceptInvitation(secondToken, Anna, DisplayName.From("Anna Berger"), AnnaMail, Now).IsSuccess.ShouldBeTrue();

        first.Memberships.Single(m => m.Id == firstMembership).Status.ShouldBe(MembershipStatus.Active);
        first.Memberships.Single(m => m.Id == firstMembership).Person.ShouldBe(Anna);
        second.Memberships.Single(m => m.Id == secondMembership).Status.ShouldBe(MembershipStatus.Active);
        second.Memberships.Single(m => m.Id == secondMembership).Person.ShouldBe(Anna);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("forwarded")]
    [InlineData("already-member")]
    public void A_refused_accept_raises_nothing_and_changes_nothing(string why)
    {
        var pharmacy = Register();
        var token = Token();
        var membership = Invite(pharmacy, AnnaMail, token);
        var person = why == "already-member" ? Owner : Anna;
        var email = why == "forwarded" ? EmailAddress.From("bert@example.org") : AnnaMail;
        var now = why == "expired" ? Now.PlusDays(15) : Now;
        var presented = why == "unknown" ? Token() : token;
        pharmacy.ClearDomainEvents();

        pharmacy.AcceptInvitation(presented, person, DisplayName.From("Anna Berger"), email, now).IsFailure.ShouldBeTrue();

        pharmacy.DomainEvents.ShouldBeEmpty();
        var open = pharmacy.Memberships.Single(m => m.Id == membership);
        open.Status.ShouldBe(MembershipStatus.Invited);
        open.InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        open.Person.ShouldBeNull();
        open.Name.ShouldBeNull();
        open.StartedAt.ShouldBeNull();
        open.InvitationExpiresAt.ShouldBe(Now.PlusDays(Pharmacy.InvitationValidityDays));
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
    public void Issuing_a_link_stores_the_hash_of_its_token_and_answers_the_expiry()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        pharmacy.ClearDomainEvents();
        var token = Token();

        var issued = pharmacy.IssueInvitationLink(membership, token, Later(1));

        issued.IsSuccess.ShouldBeTrue();
        issued.Value.ShouldBe(Now.PlusDays(14));
        pharmacy.Memberships.Single(m => m.Id == membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        var raised = pharmacy.DomainEvents.OfType<InvitationLinkIssued>().SingleOrDefault();
        raised.ShouldNotBeNull("no InvitationLinkIssued raised");
        raised.Pharmacy.ShouldBe(pharmacy.Id);
        raised.Membership.ShouldBe(membership);
    }

    /// <summary>
    /// A link issued again for the same invitation version joins the one before, and each opens the invitation: the
    /// invitee may hold either mail. A renewal clears both.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Every_link_issued_for_one_version_works(int accepted)
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        InvitationToken[] links = [Token(), Token()];

        pharmacy.IssueInvitationLink(membership, links[0], Later(1)).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, links[1], Later(2)).IsSuccess.ShouldBeTrue();

        pharmacy.Memberships.Single(m => m.Id == membership).InvitationLinks.Select(link => link.Hash)
            .ShouldBe([InvitationTokenHash.Of(links[0]), InvitationTokenHash.Of(links[1])]);
        pharmacy.AcceptInvitation(links[accepted], Anna, DisplayName.From("Anna Berger"), AnnaMail, Later(3)).IsSuccess
            .ShouldBeTrue($"link {accepted + 1} of 2 issued for one version does not open the invitation");
    }

    [Fact]
    public void A_renewal_clears_every_link_issued_for_the_version_before()
    {
        var pharmacy = Register();
        var membership = Invite(pharmacy, AnnaMail);
        InvitationToken[] links = [Token(), Token()];
        pharmacy.IssueInvitationLink(membership, links[0], Later(1)).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, links[1], Later(2)).IsSuccess.ShouldBeTrue();

        pharmacy.RenewInvitation(membership, Later(3)).IsSuccess.ShouldBeTrue();

        var renewed = pharmacy.Memberships.Single(m => m.Id == membership);
        renewed.InvitationLinks.ShouldBeEmpty();
        renewed.InvitationVersion.ShouldBe(InvitationVersion.From(2));
        foreach (var link in links)
        {
            pharmacy.AcceptInvitation(link, Anna, DisplayName.From("Anna Berger"), AnnaMail, Later(4)).Error.Value.ShouldBeOfType<InvitationNotFound>();
        }
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
        List<(MembershipId Membership, InvitationTokenHash Hash)> before = [.. pharmacy.Memberships.SelectMany(m => m.InvitationLinks, (m, link) => (m.Id, link.Hash))];
        pharmacy.ClearDomainEvents();

        var issued = pharmacy.IssueInvitationLink(membership, Token(), Later(1));

        issued.IsSuccess.ShouldBeFalse($"a link was issued for the {which} membership: should be NoOpenInvitation but was SuccessOutcome");
        issued.Error.ShouldBeOfType<NoOpenInvitation>();
        pharmacy.Memberships.SelectMany(m => m.InvitationLinks, (m, link) => (m.Id, link.Hash)).ShouldBe(before);
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
        pharmacy.Memberships.Single(m => m.Id == membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(token)]);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void InvitationLinkIssued_carries_no_token()
    {
        typeof(InvitationLinkIssued).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["Membership", "Pharmacy"]);
    }

    [Fact]
    public void An_invitation_holds_no_link_until_its_mail_issues_one()
    {
        var pharmacy = Register();
        var membership = MembershipId.New();

        pharmacy.Invite(membership, AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();

        var invited = pharmacy.Memberships.Single(m => m.Id == membership);
        invited.InvitationLinks.ShouldBeEmpty();
        invited.InvitationExpiresAt.ShouldBe(Now.PlusDays(14));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void A_renewal_kills_the_old_link_at_once_and_runs_14_days_from_the_renewal(int daysLater)
    {
        var pharmacy = Register();
        var membership = MembershipId.New();
        pharmacy.Invite(membership, AnnaMail, JobFunction.Pta, MembershipRole.Employee, Now).IsSuccess.ShouldBeTrue();
        var first = Token();
        pharmacy.IssueInvitationLink(membership, first, Later(1)).IsSuccess.ShouldBeTrue();
        var later = Now.PlusDays(daysLater);

        pharmacy.RenewInvitation(membership, later).IsSuccess.ShouldBeTrue();

        var renewed = pharmacy.Memberships.Single(m => m.Id == membership);
        renewed.InvitationLinks.ShouldBeEmpty();
        renewed.InvitationExpiresAt.ShouldBe(later.PlusDays(14));
        pharmacy.AcceptInvitation(first, Anna, DisplayName.From("Anna Berger"), AnnaMail, later).Error.Value.ShouldBeOfType<InvitationNotFound>();
    }

    [Fact]
    public void MemberInvited_carries_no_token_address_or_name()
    {
        var properties = typeof(MemberInvited).GetProperties().Select(property => property.Name).ToList();

        properties.ShouldBe(["Pharmacy", "Membership", "ExpiresAt"], ignoreOrder: true, $"MemberInvited has properties {string.Join(", ", properties)}");
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
