using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;

/// <summary>A manager of the pharmacy withdraws an open invitation; its link stops working and the address is free again.</summary>
public sealed record RevokeInvitation(PharmacyId Pharmacy, MembershipId Membership, PersonId By) : ICommand<Result<Unit, ApiError>>;
