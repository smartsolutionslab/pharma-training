using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// A pharmacy releases a person for erasure (#115): it ends their active membership, unless they are its last active
/// manager, and revokes the open invitations to their addresses; later it forgets the name and address of every closed
/// membership of theirs, and skips one that is open again.
/// </summary>
public class ErasePersonTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly Timestamp Now = TestPharmacies.RegisteredAt.PlusDays(3);

    [Fact]
    public void Releasing_the_last_active_manager_is_refused_and_changes_nothing()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var founder = TestPharmacies.FounderOf(pharmacy);
        var ownerAddress = pharmacy.Membership(founder).Email!;
        var invitation = TestPharmacies.Invite(pharmacy, "second-address@example.org");
        pharmacy.ClearDomainEvents();

        var refused = pharmacy.ReleaseForErasure(Owner, [ownerAddress, EmailAddress.From("second-address@example.org")], Now);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.ShouldBeOfType<LastManagerCannotLeave>();
        pharmacy.Membership(founder).Status.ShouldBe(MembershipStatus.Active);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Invited, "a refusal revokes nothing either");
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Releasing_a_manager_while_another_remains_ends_the_membership()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Manager);
        pharmacy.ClearDomainEvents();

        var released = pharmacy.ReleaseForErasure(person, [pharmacy.Membership(membership).Email!], Now);

        released.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(membership).Status.ShouldBe(MembershipStatus.Ended);
        pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)).Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public void Releasing_ends_the_active_membership_and_raises_MembershipEnded()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee);
        pharmacy.ClearDomainEvents();

        var released = pharmacy.ReleaseForErasure(person, [pharmacy.Membership(membership).Email!], Now);

        released.IsSuccess.ShouldBeTrue();
        released.Value.ShouldBe(Unit.Value);
        var ended = pharmacy.Membership(membership);
        ended.Status.ShouldBe(MembershipStatus.Ended);
        ended.EndedAt.ShouldBe(Now);
        pharmacy.DomainEvents.OfType<MembershipEnded>().ShouldHaveSingleItem()
            .ShouldBe(new MembershipEnded(pharmacy.Id, membership, person, Now));
    }

    [Fact]
    public void Releasing_revokes_open_invitations_to_the_addresses_and_clears_their_links()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var toPerson = TestPharmacies.Invite(pharmacy, "erased@example.org");
        pharmacy.IssueInvitationLink(toPerson, Token(), TestPharmacies.RegisteredAt).IsSuccess.ShouldBeTrue();
        var toSomeoneElse = TestPharmacies.Invite(pharmacy, "someone-else@example.org");
        pharmacy.ClearDomainEvents();

        var released = pharmacy.ReleaseForErasure(person, [EmailAddress.From("erased@example.org")], Now);

        released.IsSuccess.ShouldBeTrue();
        var revoked = pharmacy.Membership(toPerson);
        revoked.Status.ShouldBe(MembershipStatus.Revoked);
        revoked.InvitationLinks.ShouldBeEmpty();
        pharmacy.Membership(toSomeoneElse).Status.ShouldBe(MembershipStatus.Invited);
        pharmacy.DomainEvents.OfType<InvitationRevoked>().ShouldHaveSingleItem().ShouldBe(new InvitationRevoked(pharmacy.Id, toPerson));
        pharmacy.DomainEvents.OfType<MembershipEnded>().ShouldBeEmpty();
    }

    [Fact]
    public void Releasing_a_person_with_no_membership_here_changes_nothing()
    {
        var stranger = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var member = TestPharmacies.AddMember(pharmacy, PersonId.From(Guid.CreateVersion7()), MembershipRole.Employee);
        var invitation = TestPharmacies.Invite(pharmacy, "someone-else@example.org");
        pharmacy.ClearDomainEvents();

        var released = pharmacy.ReleaseForErasure(stranger, [EmailAddress.From("stranger@example.org")], Now);

        released.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(member).Status.ShouldBe(MembershipStatus.Active);
        pharmacy.Membership(invitation).Status.ShouldBe(MembershipStatus.Invited);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Forgetting_clears_name_and_address_of_ended_and_revoked_memberships()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee, name: "Erika Erased");
        var address = pharmacy.Membership(membership).Email!;
        var other = TestPharmacies.AddMember(pharmacy, PersonId.From(Guid.CreateVersion7()), MembershipRole.Employee, name: "Karl Kept");
        var invitation = TestPharmacies.Invite(pharmacy, "erika.second@example.org");
        pharmacy.ReleaseForErasure(person, [address, EmailAddress.From("erika.second@example.org")], Now).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        var skipped = pharmacy.ForgetPerson(person, [address, EmailAddress.From("erika.second@example.org")]);

        skipped.ShouldBeEmpty();
        var ended = pharmacy.Membership(membership);
        ended.Status.ShouldBe(MembershipStatus.Ended);
        ended.Name.ShouldBeNull();
        ended.Email.ShouldBeNull();
        ended.Person.ShouldBe(person, "the PersonId stays behind as the pseudonym");
        var revoked = pharmacy.Membership(invitation);
        revoked.Status.ShouldBe(MembershipStatus.Revoked);
        revoked.Email.ShouldBeNull();
        pharmacy.Membership(other).Name.ShouldBe(DisplayName.From("Karl Kept"));
        pharmacy.Membership(other).Email.ShouldNotBeNull();
        pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)).Email.ShouldNotBeNull();
    }

    [Fact]
    public void Forgetting_skips_a_membership_that_is_still_open()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee, name: "Erika Erased");
        var address = pharmacy.Membership(membership).Email!;
        pharmacy.ReleaseForErasure(person, [address], Now).IsSuccess.ShouldBeTrue();
        var reinvited = TestPharmacies.Invite(pharmacy, address.Value, Now);
        pharmacy.ClearDomainEvents();

        var skipped = pharmacy.ForgetPerson(person, [address]);

        skipped.ShouldBe([reinvited]);
        pharmacy.Membership(reinvited).Status.ShouldBe(MembershipStatus.Invited);
        pharmacy.Membership(reinvited).Email.ShouldBe(address);
        pharmacy.Membership(membership).Name.ShouldBeNull();
        pharmacy.Membership(membership).Email.ShouldBeNull();
    }

    [Fact]
    public void Forgetting_skips_an_active_membership_of_the_person()
    {
        var person = PersonId.From(Guid.CreateVersion7());
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.AddMember(pharmacy, person, MembershipRole.Employee, name: "Erika Erased");
        var address = pharmacy.Membership(membership).Email!;

        var skipped = pharmacy.ForgetPerson(person, [address]);

        skipped.ShouldBe([membership]);
        pharmacy.Membership(membership).Name.ShouldBe(DisplayName.From("Erika Erased"));
        pharmacy.Membership(membership).Email.ShouldBe(address);
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
