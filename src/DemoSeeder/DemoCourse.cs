namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// A course: its first version's chapters and blueprint, and the chapters its second version adds (none for a course with
/// one version).
/// </summary>
// stub for #457
public sealed record DemoCourse(string Title, IReadOnlyList<DemoChapter> Chapters, DemoBlueprint Blueprint, IReadOnlyList<DemoChapter> SecondVersionChapters);
