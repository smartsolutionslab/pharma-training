using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event whose name is hidden in the records of a list it carries.</summary>
public sealed record ProbeWithNestedNameV1(Guid SessionId, IReadOnlyList<ProbeAttendeeV1> Attendees) : IntegrationEvent;
