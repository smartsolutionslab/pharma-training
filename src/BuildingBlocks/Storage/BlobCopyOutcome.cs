namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>What a copy into the module's own container did.</summary>
public enum BlobCopyOutcome
{
    /// <summary>The source's bytes now lie under the target name.</summary>
    Copied,

    /// <summary>A blob already lay under the target name, so nothing was copied; a retry or second delivery ends here.</summary>
    AlreadyThere,
}
