using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

/// <summary>An open invitation got a new link at the time its mail is sent. Carries no token.</summary>
[DeliberatelyUnhandled("The consumer that issued the link mails it; nothing reacts.")]
public sealed record InvitationLinkIssued(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;
