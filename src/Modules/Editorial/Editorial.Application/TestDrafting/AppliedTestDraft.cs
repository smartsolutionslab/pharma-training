using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// An AI test draft once applied to its version (#22): the ids of the questions it drafted, in the order the version
/// holds them, and the PDF and video chapters it left out, in the version's order.
/// </summary>
public sealed record AppliedTestDraft(IReadOnlyList<QuestionId> Questions, IReadOnlyList<ChapterId> LeftOutChapters);
