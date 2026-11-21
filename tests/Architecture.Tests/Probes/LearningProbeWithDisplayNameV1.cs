using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event that carries a person's name, which an event of Learning must not.</summary>
public sealed record LearningProbeWithDisplayNameV1(Guid SessionId, Guid PersonId, string DisplayName) : IntegrationEvent;
