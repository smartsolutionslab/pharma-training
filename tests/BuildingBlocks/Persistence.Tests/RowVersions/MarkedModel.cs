namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence.Tests.RowVersions;

/// <summary>Which model <see cref="MarkedModelContext"/> builds.</summary>
public enum MarkedModel
{
    OwnedLinkWithOneKey,
    EntityWithTwoKeys,
    PropertyOnTwoKeys,
}
