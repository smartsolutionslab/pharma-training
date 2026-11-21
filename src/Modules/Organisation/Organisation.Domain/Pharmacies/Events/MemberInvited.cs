using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

[DeliberatelyUnhandled("Until #9 adds the reaction that sends the invitation mail through the IdP.")]
public sealed record MemberInvited(PharmacyId Pharmacy, MembershipId Membership, EmailAddress Email, InvitationToken Token, Timestamp ExpiresAt) : IDomainEvent
{
    /// <summary>Stub for #9's red tests (T1): the pharmacy's name is not carried yet.</summary>
    public PharmacyName? Name { get; init; }
}
