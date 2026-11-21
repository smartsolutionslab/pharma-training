namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A value type inside an event that carries a name, so the rule must look into structs too.</summary>
public readonly record struct ProbeBadgeV1(Guid PersonId, string HolderName);
