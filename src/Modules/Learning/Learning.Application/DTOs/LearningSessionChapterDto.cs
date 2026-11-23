namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

/// <summary>A chapter of a learning session: its place in reading order, what it holds, how far the learner got and whether it counts as worked through.</summary>
public sealed record LearningSessionChapterDto(Guid ChapterId, int Position, LearningChapterKind Kind, int ProgressPercent, bool Completed);
