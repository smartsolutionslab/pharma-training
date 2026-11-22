using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Accepting an invitation fails in a closed set of ways, a union of their records: the domain returns it, and the
/// Application maps each case to its code and status with a switch that has no discard arm.
/// </summary>
public class AcceptInvitationErrorTests
{
    private static readonly PharmacyId Pharmacy = PharmacyId.New();

    [Fact]
    public void A_link_the_pharmacy_does_not_hold_is_not_found_exactly_as_a_link_no_pharmacy_holds()
    {
        var mapped = Mapped(new InvitationNotFound(Pharmacy));

        var unknown = PharmacyFailures.InvitationLinkNotFound();
        mapped.Code.ShouldBe("organisation.invitation-not-found");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
        mapped.Code.ShouldBe(unknown.Code);
        mapped.Message.ShouldBe(unknown.Message);
        mapped.Status.ShouldBe(unknown.Status);
        mapped.Message.ShouldNotContain(Pharmacy.Value.ToString());
    }

    [Fact]
    public void An_invitation_that_ran_out_is_gone()
    {
        var mapped = Mapped(new InvitationExpired(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.invitation-expired");
        mapped.Status.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public void A_link_for_another_address_is_forbidden()
    {
        var mapped = Mapped(new InvitationForAnotherAddress(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.invitation-for-another-address");
        mapped.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void A_person_already_active_in_the_pharmacy_is_a_conflict()
    {
        var mapped = Mapped(new PersonAlreadyMember(Pharmacy, PersonId.From(Guid.CreateVersion7())));

        mapped.Code.ShouldBe("organisation.person-already-member");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_union_holds_exactly_the_four_ways_accepting_fails()
    {
        // No discard arm: a fifth case makes this switch non-exhaustive, which the Release build refuses.
        Case(new InvitationNotFound(Pharmacy)).ShouldBe("not found");
        Case(new InvitationExpired(Pharmacy, MembershipId.New())).ShouldBe("expired");
        Case(new InvitationForAnotherAddress(Pharmacy, MembershipId.New())).ShouldBe("another address");
        Case(new PersonAlreadyMember(Pharmacy, PersonId.From(Guid.CreateVersion7()))).ShouldBe("already member");
    }

    [Fact]
    public void The_pharmacy_answers_a_refused_accept_with_the_union()
    {
        var pharmacy = TestPharmacies.Register(PersonId.From(Guid.CreateVersion7()));
        var unknown = InvitationToken.From(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

        Result<Unit, AcceptInvitationError> accepted = pharmacy.AcceptInvitation(
            unknown,
            PersonId.From(Guid.CreateVersion7()),
            DisplayName.From("Anna Berger"),
            EmailAddress.From("anna@example.org"),
            TestPharmacies.RegisteredAt);

        accepted.IsFailure.ShouldBeTrue();
        accepted.Error.Value.ShouldBeOfType<InvitationNotFound>();
        PharmacyFailures.From(accepted.Error).Code.ShouldBe("organisation.invitation-not-found");
    }

    private static ApiError Mapped(AcceptInvitationError error) => PharmacyFailures.From(error);

    private static string Case(AcceptInvitationError error) => error switch
    {
        InvitationNotFound => "not found",
        InvitationExpired => "expired",
        InvitationForAnotherAddress => "another address",
        PersonAlreadyMember => "already member",
    };
}
