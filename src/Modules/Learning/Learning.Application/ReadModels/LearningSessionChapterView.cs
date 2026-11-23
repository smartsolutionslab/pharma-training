using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>A chapter of a session's view: its kind, the furthest progress stored and whether it counts as worked through.</summary>
public sealed class LearningSessionChapterView
{
    public Guid ChapterId { get; set; }

    public ChapterKind Kind { get; set; }

    public int ProgressPercent { get; set; }

    public bool Completed { get; set; }
}
