using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.ChangeMembership;

/// <summary>A manager of the pharmacy sets an active membership's job function and role; a manager always remains.</summary>
public sealed record ChangeMembership(PharmacyId Pharmacy, MembershipId Membership, PersonId By, JobFunction JobFunction, MembershipRole Role)
    : ICommand<Result<Unit, ApiError>>;
