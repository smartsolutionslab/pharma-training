using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>An attempt started with its questions in the order it shows them. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record AttemptStarted(ExaminationId Examination, AttemptId Attempt, AttemptNumber Number, IReadOnlyList<QuestionVersionId> Questions, Timestamp StartedAt) : IDomainEvent;
