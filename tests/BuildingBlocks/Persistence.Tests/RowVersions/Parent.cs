namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

public sealed class Parent
{
    public Guid Id { get; set; }

    public List<Link> Links { get; } = [];
}
