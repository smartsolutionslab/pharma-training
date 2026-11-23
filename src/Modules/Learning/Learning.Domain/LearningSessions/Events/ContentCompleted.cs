using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

/// <summary>Every chapter of the session is worked through, so its test unlocks. Becomes ContentCompletedV1.</summary>
public sealed record ContentCompleted(LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, Timestamp CompletedAt) : IDomainEvent;
