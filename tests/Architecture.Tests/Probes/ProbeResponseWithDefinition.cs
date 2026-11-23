using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A response that hands out the version's whole test definition, solutions included.</summary>
public sealed record ProbeResponseWithDefinition(Guid ExaminationId, TestDefinition Definition);
