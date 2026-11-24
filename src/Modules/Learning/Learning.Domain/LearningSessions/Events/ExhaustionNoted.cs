using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>The session took note of the AttemptsExhausted announcement that locked its test, so the same lock acts once.</summary>
public sealed record ExhaustionNoted(LearningSessionId Session, ExhaustionNotice Notice, Timestamp NotedAt) : IDomainEvent;
