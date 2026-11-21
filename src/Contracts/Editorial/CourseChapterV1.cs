namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>A chapter of a published course version, as <see cref="CourseVersionPublishedV1"/> carries it.</summary>
/// <param name="ChapterId">The chapter's identifier.</param>
/// <param name="Position">The 1-based reading order.</param>
/// <param name="Title">The chapter's title.</param>
/// <param name="Kind">What the chapter holds.</param>
/// <param name="VideoDurationSeconds">The required viewing length of a video chapter, which Learning checks plausibility against; null for any other kind.</param>
/// <param name="Text">The Markdown of a text chapter; null for any other kind.</param>
/// <param name="File">The blob reference of a PDF or video chapter; null for a text chapter.</param>
public sealed record CourseChapterV1(Guid ChapterId, int Position, string Title, ChapterKindV1 Kind, int? VideoDurationSeconds, string? Text, string? File);
