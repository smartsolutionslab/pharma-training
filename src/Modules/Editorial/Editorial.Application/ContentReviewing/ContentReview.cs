using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// A checked AI content review: the chapters that were reviewed (the chapters with text, in the version's order), the
/// hints on them, ordered by chapter and in the model's order within one, the chapters without text it left out (videos,
/// PDFs without text) and the PDF chapters it reviewed from text cut at a limit, each in the version's order.
/// </summary>
public sealed record ContentReview(
    IReadOnlyList<ChapterId> Reviewed,
    IReadOnlyList<ContentHint> Hints,
    IReadOnlyList<ChapterId> LeftOutChapters,
    IReadOnlyList<ChapterId> TruncatedChapters);
