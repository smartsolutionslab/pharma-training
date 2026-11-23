using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>An integration event outside every module's contract namespace, which no service may publish.</summary>
public sealed record ProbeStrayEventV1(Guid Id) : IntegrationEvent;
