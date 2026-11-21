using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event that carries a person's e-mail address, which an event of Assessment must not.</summary>
public sealed record AssessmentProbeWithEmailV1(Guid AttemptId, Guid PersonId, string Email) : IntegrationEvent;
