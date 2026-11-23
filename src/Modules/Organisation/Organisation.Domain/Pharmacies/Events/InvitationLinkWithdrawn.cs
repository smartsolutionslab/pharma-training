using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

/// <summary>A link whose mail never reached the mail server was taken back. Carries no token.</summary>
[DeliberatelyUnhandled("The consumer that issued the link withdraws it and retries; nothing reacts.")]
public sealed record InvitationLinkWithdrawn(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;
