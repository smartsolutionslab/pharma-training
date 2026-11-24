namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// An AI content review (ADR 0009 D1): the chapters that were reviewed, so a screen can tell "no hints" from "not
/// reviewed", the hints on them, ordered by chapter, and the chapters it could not read in full.
/// </summary>
/// <param name="ReviewedChapterIds">The text chapters and the PDF chapters with text that were sent, in the version's order.</param>
/// <param name="Hints">The hints on the reviewed chapters, ordered by chapter.</param>
/// <param name="LeftOutChapterIds">The chapters the model was not asked about, in the version's order: video chapters, PDF chapters without text, and PDF chapters left out for the content limit. Empty when every chapter was sent.</param>
/// <param name="TruncatedChapterIds">The PDF chapters reviewed from text cut at a limit, in the version's order, whose later pages were not reviewed. Empty when no text was cut.</param>
public sealed record EditorialContentReviewDto(
    IReadOnlyList<Guid> ReviewedChapterIds,
    IReadOnlyList<EditorialContentHintDto> Hints,
    IReadOnlyList<Guid> LeftOutChapterIds,
    IReadOnlyList<Guid> TruncatedChapterIds);
