namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>A domain error whose message names the id it was made with, which EveryErrorCodeHasOneMessageAndStatus names.</summary>
public sealed record ProbeErrorNamingItsId(Guid Id) : DomainError("probe.naming-its-id", $"There is no probe {Id}.");
