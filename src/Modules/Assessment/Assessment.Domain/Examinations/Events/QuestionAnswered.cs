using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>The learner chose these options for a question of the open attempt; a later answer replaces it. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record QuestionAnswered(ExaminationId Examination, AttemptId Attempt, QuestionVersionId Question, IReadOnlyList<AnswerOptionId> Chosen, Timestamp AnsweredAt) : IDomainEvent;
