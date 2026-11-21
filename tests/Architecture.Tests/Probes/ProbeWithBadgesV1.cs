using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An event whose name is hidden in a struct it carries in a list.</summary>
public sealed record ProbeWithBadgesV1(Guid SessionId, IReadOnlyList<ProbeBadgeV1> Badges) : IntegrationEvent;
