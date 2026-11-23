using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>A chapter of Learning's local copy of a course version: where it stands in reading order and what it holds.</summary>
public sealed class CourseVersionChapterCopy
{
    public Guid CourseVersionId { get; set; }

    public Guid ChapterId { get; set; }

    public int Position { get; set; }

    public ChapterKind Kind { get; set; }

    public int? VideoDurationSeconds { get; set; }
}
