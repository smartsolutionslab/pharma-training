using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CancelAssignment;

/// <summary>A member who may assign courses in the pharmacy withdraws one of its assignments.</summary>
public sealed record CancelAssignment(PharmacyId Pharmacy, AssignmentId Assignment, PersonId By)
    : ICommand<Result<Unit, ApiError>>;
