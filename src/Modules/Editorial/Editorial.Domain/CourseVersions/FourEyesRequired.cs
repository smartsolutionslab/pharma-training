namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record FourEyesRequired(CourseVersionId Version)
    : DomainError("editorial.four-eyes", "Under four eyes, the editor who submitted this version cannot publish it.");
