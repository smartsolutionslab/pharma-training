namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record TestIncomplete(CourseVersionId Version, QuestionCount Expected, QuestionCount Actual)
    : DomainError("editorial.test-incomplete", $"Der Test braucht {Expected.Value} Fragen.");
