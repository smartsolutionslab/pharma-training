namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>What a run created: users, pharmacies, memberships, course versions and their content, assignments, sessions and attempts.</summary>
internal sealed class SeedCount
{
    public int Created { get; private set; }

    public void Add(int created = 1) => Created += created;
}
