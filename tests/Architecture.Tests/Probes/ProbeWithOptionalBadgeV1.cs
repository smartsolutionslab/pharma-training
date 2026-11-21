using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event whose name is hidden in a struct it may or may not carry.</summary>
public sealed record ProbeWithOptionalBadgeV1(Guid SessionId, ProbeBadgeV1? Badge) : IntegrationEvent;
