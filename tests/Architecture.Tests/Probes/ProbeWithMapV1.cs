using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event whose name is hidden in the values of a dictionary it carries.</summary>
public sealed record ProbeWithMapV1(Guid SessionId, IReadOnlyDictionary<Guid, ProbeAttendeeV1> Attendees) : IntegrationEvent;
