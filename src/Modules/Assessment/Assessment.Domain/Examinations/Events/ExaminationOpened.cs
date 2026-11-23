using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>The learner completed a session's content for the first time: the session's examination exists and is unlocked. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record ExaminationOpened(ExaminationId Examination, LearningSessionId Session, PersonId Learner, CourseId Course, CourseVersionId CourseVersion, Timestamp OpenedAt) : IDomainEvent;
