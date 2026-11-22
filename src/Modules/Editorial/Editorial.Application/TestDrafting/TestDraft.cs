using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// An AI test draft (ADR 0008): the drafted questions, and the PDF and video chapters it left out, in the version's
/// order, so the editor knows which chapters still need questions written by hand.
/// </summary>
public sealed record TestDraft(IReadOnlyList<DraftedQuestion> Questions, IReadOnlyList<ChapterId> LeftOutChapters);
