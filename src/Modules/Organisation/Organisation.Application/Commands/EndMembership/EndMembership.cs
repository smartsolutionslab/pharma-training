using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;

/// <summary>A manager of the pharmacy ends one of its active memberships, their own included while another manager remains.</summary>
public sealed record EndMembership(PharmacyId Pharmacy, MembershipId Membership, PersonId By) : ICommand<Result<Unit, ApiError>>;
