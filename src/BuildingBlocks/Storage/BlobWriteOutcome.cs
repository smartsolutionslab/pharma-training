namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>What a create-only write into the module's own container did.</summary>
public enum BlobWriteOutcome
{
    /// <summary>The bytes given now lie under the name.</summary>
    Created,

    /// <summary>A blob already lay under the name and was kept as it is; a retry or second delivery ends here.</summary>
    AlreadyThere,
}
