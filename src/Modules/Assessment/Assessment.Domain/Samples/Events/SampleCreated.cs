using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples.Events;

/// <summary>A sample was created with this name. A stored event is never edited: a change is a new event.</summary>
public sealed record SampleCreated(SampleId Id, SampleName Name) : IDomainEvent;
