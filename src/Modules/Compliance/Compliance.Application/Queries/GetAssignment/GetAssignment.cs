using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetAssignment;

/// <summary>A member who may assign courses in the pharmacy reads one of its assignments with its history.</summary>
public sealed record GetAssignment(PharmacyId Pharmacy, AssignmentId Assignment, PersonId By)
    : IQuery<Result<ComplianceAssignmentDto, ApiError>>;
