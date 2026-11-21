using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Nested;

/// <summary>An event in a namespace below the probes' own, as an event in a subfolder of a module's contracts would be.</summary>
public sealed record NestedProbeWithIdsV1(Guid SessionId, Guid PersonId) : IntegrationEvent;
