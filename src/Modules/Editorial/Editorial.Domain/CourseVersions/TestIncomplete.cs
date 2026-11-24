namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record TestIncomplete(CourseVersionId Version, QuestionCount Expected, QuestionCount Actual)
    : DomainError("editorial.test-incomplete", "The test does not have the number of questions its blueprint asks for.");
