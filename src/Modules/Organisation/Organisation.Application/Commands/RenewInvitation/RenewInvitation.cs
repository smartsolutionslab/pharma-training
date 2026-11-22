using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;

/// <summary>A manager of the pharmacy sends a fresh link for an invitation that is open or has run out.</summary>
public sealed record RenewInvitation(PharmacyId Pharmacy, MembershipId Membership, PersonId By) : ICommand<Result<Unit, ApiError>>;
