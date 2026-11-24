namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// What applying an AI test draft answers (ADR 0008 D10): identifiers only, as a command may return. The editor reads
/// the questions themselves back with the version.
/// </summary>
/// <param name="DraftedQuestionIds">The new, unreviewed questions, in the order the version now holds them, after the approved ones it kept. Never empty.</param>
/// <param name="LeftOutChapterIds">The video chapters and PDF chapters without text the model was not asked about, in the version's order, which still need questions written by hand. Empty when every chapter has text.</param>
/// <param name="TruncatedChapterIds">The PDF chapters drafted from text cut at a limit, in the version's order, whose later pages may still need questions written by hand. Empty when no text was cut.</param>
public sealed record EditorialTestDraftDto(IReadOnlyList<Guid> DraftedQuestionIds, IReadOnlyList<Guid> LeftOutChapterIds, IReadOnlyList<Guid> TruncatedChapterIds);
