using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>The open attempt was scored on the server; a failed one names the chapters of its wrong answers. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record AttemptSubmitted(ExaminationId Examination, AttemptId Attempt, ScorePercent Score, AttemptOutcome Outcome, IReadOnlyList<ChapterId> FailedChapters, Timestamp SubmittedAt) : IDomainEvent;
