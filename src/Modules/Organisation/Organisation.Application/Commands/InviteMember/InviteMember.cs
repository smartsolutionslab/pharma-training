using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;

/// <summary>A manager of the pharmacy invites an address to join it with a job function and a role.</summary>
public sealed record InviteMember(PharmacyId Pharmacy, PersonId By, EmailAddress Email, JobFunction JobFunction, MembershipRole Role)
    : ICommand<Result<Guid, ApiError>>;
