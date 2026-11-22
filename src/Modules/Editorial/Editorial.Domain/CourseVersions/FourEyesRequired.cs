namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record FourEyesRequired(CourseVersionId Version)
    : CourseVersionWorkflowError("editorial.four-eyes", "Veröffentlichen muss eine andere Person als die, die eingereicht hat.");
