namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// Learning's local copy of a course version Editorial published: its course, its number and its chapters, which a
/// session takes its chapter structure from at start. Written once, by the consumer of <c>CourseVersionPublishedV1</c>.
/// </summary>
public sealed class CourseVersionCopy
{
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public int VersionNumber { get; set; }

    public List<CourseVersionChapterCopy> Chapters { get; set; } = [];
}
