using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>A checked hint on one chapter: its kind and its trimmed text, model output to be shown as plain text.</summary>
public sealed record ContentHint(ChapterId Chapter, ContentHintKind Kind, string Text)
{
    /// <summary>The longest hint text accepted, after trimming; the instruction asks for at most 300 characters.</summary>
    public const int MaxTextLength = 1000;
}
