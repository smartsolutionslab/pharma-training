using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

/// <summary>The mail of an invitation's version was handed to the mail server; that version is not mailed again.</summary>
[DeliberatelyUnhandled("The consumer that sent the mail records it; nothing reacts.")]
public sealed record InvitationMailed(PharmacyId Pharmacy, MembershipId Membership, InvitationVersion Version) : IDomainEvent;
