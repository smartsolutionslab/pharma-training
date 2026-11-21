using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples.Events;

/// <summary>A sample was created with this name. A stored event is never edited: a change is a new event.</summary>
public sealed record SampleCreated(SampleId Id, SampleName Name) : IDomainEvent;
