namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A pharmacy, registered by its manager, who invites its employees.</summary>
public sealed record DemoPharmacy(string Name, string Manager, IReadOnlyList<DemoEmployee> Employees);
