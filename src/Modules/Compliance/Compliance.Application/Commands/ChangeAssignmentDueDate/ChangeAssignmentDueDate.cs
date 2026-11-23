using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.ChangeAssignmentDueDate;

/// <summary>A member who may assign courses in the pharmacy moves one of its assignments to another due date, perhaps saying why.</summary>
public sealed record ChangeAssignmentDueDate(PharmacyId Pharmacy, AssignmentId Assignment, DueDate DueDate, ChangeReason? Reason, PersonId By)
    : ICommand<Result<Unit, ApiError>>;
