namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

public sealed record DueDateInThePast(DueDate DueDate)
    : DomainError("compliance.due-date-in-the-past", $"Die Frist {DueDate.Value.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)} liegt in der Vergangenheit.");
