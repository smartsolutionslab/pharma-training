namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A text chapter and the questions tied to it.</summary>
// stub for #457
public sealed record DemoChapter(string Title, string Text, IReadOnlyList<DemoQuestion> Questions);
