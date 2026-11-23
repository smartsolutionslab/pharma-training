namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>When a chapter counts as worked through. Video: 95 % (seeking is limited to what was seen); text and PDF: the end.</summary>
public static class CompletionRule
{
    public static ProgressPercent ThresholdFor(ChapterKind kind) =>
        kind == ChapterKind.Video ? ProgressPercent.From(95) : ProgressPercent.Complete;
}
