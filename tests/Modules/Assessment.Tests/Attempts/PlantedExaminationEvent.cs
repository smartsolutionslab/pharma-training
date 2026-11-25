using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// A stored event of an examination with no marker (#307), as a test plants it: the event, its examination as the stream
/// held it, its version in the examination's stream and when it was stored.
/// </summary>
internal sealed record PlantedExaminationEvent<TEvent>(TEvent Event, Examination Examination, long StreamVersion, DateTimeOffset StoredAt)
    where TEvent : class, IDomainEvent;
