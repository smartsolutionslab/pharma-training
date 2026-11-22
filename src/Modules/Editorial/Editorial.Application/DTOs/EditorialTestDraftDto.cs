namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// What applying an AI test draft answers (ADR 0008 D10): identifiers only, as a command may return. The editor reads
/// the questions themselves back with the version.
/// </summary>
/// <param name="DraftedQuestionIds">The new, unreviewed questions, in the order the version now holds them, after the approved ones it kept. Never empty.</param>
/// <param name="LeftOutChapterIds">The PDF and video chapters the model was not asked about, in the version's order, which still need questions written by hand. Empty when every chapter is text.</param>
public sealed record EditorialTestDraftDto(IReadOnlyList<Guid> DraftedQuestionIds, IReadOnlyList<Guid> LeftOutChapterIds);
