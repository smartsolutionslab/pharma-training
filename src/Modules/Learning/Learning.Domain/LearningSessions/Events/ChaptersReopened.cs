using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>The chapters of wrongly answered questions open again after the attempts ran out.</summary>
public sealed record ChaptersReopened(LearningSessionId Session, IReadOnlyList<ChapterId> Chapters, Timestamp ReopenedAt) : IDomainEvent;
