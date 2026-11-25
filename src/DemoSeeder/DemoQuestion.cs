namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A single-choice question and its options.</summary>
// stub for #457
public sealed record DemoQuestion(string Text, IReadOnlyList<DemoOption> Options);
