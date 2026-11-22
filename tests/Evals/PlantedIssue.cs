using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// An issue written into an AI content review example on purpose: its kind and the numbers of the text chapters it is
/// on. It counts as found when any hint of that kind names any of those chapters.
/// </summary>
public sealed record PlantedIssue(ContentHintKind Kind, IReadOnlyList<int> Chapters);
