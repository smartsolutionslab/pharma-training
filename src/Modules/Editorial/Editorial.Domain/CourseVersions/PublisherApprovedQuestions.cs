namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record PublisherApprovedQuestions(CourseVersionId Version)
    : CourseVersionWorkflowError("editorial.publisher-approved-questions", "Under four eyes, an editor who approved a question of this version cannot publish it.");
