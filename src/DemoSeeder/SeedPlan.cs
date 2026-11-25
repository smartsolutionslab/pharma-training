namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>What the seeder makes, as data: the users, the pharmacies, the courses and the scenarios driven on them.</summary>
public sealed record SeedPlan(
    DemoEditors Editors,
    IReadOnlyList<DemoUser> Users,
    IReadOnlyList<DemoPharmacy> Pharmacies,
    IReadOnlyList<DemoCourse> Courses,
    IReadOnlyList<DemoScenario> Scenarios)
{
    /// <summary>The demo data of #457.</summary>
    // stub for #457
    public static SeedPlan Demo => throw new NotImplementedException("stub for #457");
}
