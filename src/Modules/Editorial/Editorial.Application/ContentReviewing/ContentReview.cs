using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// A checked AI content review: the chapters that were reviewed (the text chapters, in the version's order) and the
/// hints on them, ordered by chapter and in the model's order within one.
/// </summary>
public sealed record ContentReview(IReadOnlyList<ChapterId> Reviewed, IReadOnlyList<ContentHint> Hints);
