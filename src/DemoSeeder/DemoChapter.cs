namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A text chapter and the questions tied to it.</summary>
public sealed record DemoChapter(string Title, string Text, IReadOnlyList<DemoQuestion> Questions);
