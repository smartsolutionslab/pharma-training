using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>A chapter reached its kind's threshold and counts as worked through.</summary>
public sealed record ChapterCompleted(LearningSessionId Session, ChapterId Chapter, Timestamp CompletedAt) : IDomainEvent;
