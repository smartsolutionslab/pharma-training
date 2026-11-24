namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record DueDateInThePast(DueDate DueDate)
    : DomainError("compliance.due-date-in-the-past", "The due date is in the past.");
