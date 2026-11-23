namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>What a chapter holds, which decides when it counts as worked through (<see cref="CompletionRule"/>).</summary>
public enum ChapterKind
{
    Text,
    Pdf,
    Video,
}
