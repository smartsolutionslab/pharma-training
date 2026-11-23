using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>The last allowed attempt failed: no attempt until the content is completed again. Names every chapter failed since the last unlock; leads to <c>AttemptsExhaustedV1</c>. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record ExaminationExhausted(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseVersionId CourseVersion, IReadOnlyList<ChapterId> FailedChapters, Timestamp ExhaustedAt) : IDomainEvent;
