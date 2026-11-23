using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>An attempt passed: the examination is over. Leads to <c>TestPassedV1</c>. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record ExaminationPassed(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, AttemptId Attempt, ScorePercent Score, Timestamp PassedAt) : IDomainEvent;
