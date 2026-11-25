namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>How many questions an attempt asks, the passing score in percent, and how long a certificate is valid.</summary>
// stub for #457
public sealed record DemoBlueprint(int Questions, int PassingScorePercent, int ValidityMonths);
