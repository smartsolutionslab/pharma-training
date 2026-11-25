namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A pharmacy, registered by its manager, who invites its employees.</summary>
// stub for #457
public sealed record DemoPharmacy(string Name, string Manager, IReadOnlyList<DemoEmployee> Employees);
