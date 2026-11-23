using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>The learner got further in a chapter than ever before.</summary>
public sealed record ChapterProgressRecorded(LearningSessionId Session, ChapterId Chapter, ProgressPercent Progress, Timestamp RecordedAt) : IDomainEvent;
