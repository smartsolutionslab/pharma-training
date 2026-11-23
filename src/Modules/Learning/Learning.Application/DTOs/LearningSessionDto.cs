namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

/// <summary>A learning session as its learner reads it: its version, where it stands, and its chapters in reading order.</summary>
public sealed record LearningSessionDto(Guid Id, Guid CourseId, Guid CourseVersionId, LearningSessionStatus Status, IReadOnlyList<LearningSessionChapterDto> Chapters);
