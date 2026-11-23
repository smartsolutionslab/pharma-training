using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>The test was passed; the session never changes again.</summary>
public sealed record SessionFinished(LearningSessionId Session, Timestamp FinishedAt) : IDomainEvent;
