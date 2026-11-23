namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// That Editorial retired a course version. Kept apart from the version, so it is stored even when it arrives before
/// the version it retires.
/// </summary>
public sealed class CourseVersionRetirement
{
    public Guid CourseVersionId { get; set; }

    public Guid CourseId { get; set; }
}
