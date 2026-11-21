namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

/// <summary>A chapter of a stored course version.</summary>
public sealed class CourseChapter
{
    public Guid CourseVersionId { get; set; }

    public Guid ChapterId { get; set; }

    /// <summary>The 1-based reading order.</summary>
    public int Position { get; set; }

    public string Title { get; set; } = string.Empty;

    public ChapterKind Kind { get; set; }

    /// <summary>The required viewing length of a video chapter; null for any other kind.</summary>
    public int? VideoDurationSeconds { get; set; }

    /// <summary>The Markdown of a text chapter; null for any other kind.</summary>
    public string? Text { get; set; }

    /// <summary>The blob reference of a PDF or video chapter; kept, never answered.</summary>
    public string? File { get; set; }
}
