namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>A user of the realm the seeder ensures; <paramref name="RealmRole"/> is null for none.</summary>
// stub for #457
public sealed record DemoUser(string Username, string FirstName, string LastName, string Email, string? RealmRole);
