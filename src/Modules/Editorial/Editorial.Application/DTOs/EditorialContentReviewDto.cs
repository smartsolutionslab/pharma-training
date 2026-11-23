namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// An AI content review: the chapters that were reviewed (text chapters only, so a screen can tell "no hints" from "not
/// reviewed") and the hints on them, ordered by chapter.
/// </summary>
public sealed record EditorialContentReviewDto(IReadOnlyList<Guid> ReviewedChapterIds, IReadOnlyList<EditorialContentHintDto> Hints);
