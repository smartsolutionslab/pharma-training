namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>An assignment with its stored status and its history, oldest change first.</summary>
public sealed record ComplianceAssignmentDto(
    Guid Id,
    Guid PharmacyId,
    Guid LearnerId,
    Guid CourseId,
    DateOnly DueDate,
    int? RecurrenceMonths,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    ComplianceAssignmentStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ComplianceAssignmentChangeDto> History);
