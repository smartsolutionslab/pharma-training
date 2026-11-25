namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>An employee of a pharmacy, by username, invited with a job function.</summary>
public sealed record DemoEmployee(string Username, DemoJobFunction JobFunction);
