using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

/// <summary>The learner completed the content again after an exhausted examination: three new attempts. Stored forever under its type name: never rename or edit it, add a new event instead.</summary>
public sealed record ExaminationUnlocked(ExaminationId Examination, Timestamp UnlockedAt) : IDomainEvent;
