namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

public sealed class Child
{
    public Guid Id { get; set; }

    public Guid ParentId { get; set; }

    public Guid OtherId { get; set; }

    public string Note { get; set; } = string.Empty;
}
