namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record AssignmentNotActive(AssignmentId Assignment, AssignmentStatus Status)
    : DomainError("compliance.assignment-not-active", "The assignment is no longer open.");
