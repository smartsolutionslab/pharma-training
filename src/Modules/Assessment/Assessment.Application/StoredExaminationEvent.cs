using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>A stored event of an examination, with the examination as its stream held it once the event was stored (#307).</summary>
/// <typeparam name="TEvent">The stored event.</typeparam>
public sealed record StoredExaminationEvent<TEvent>(TEvent Event, Examination Examination)
    where TEvent : class, IDomainEvent;
