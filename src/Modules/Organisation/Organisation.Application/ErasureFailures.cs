using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Everything the erasure's use cases refuse, each with its own code.</summary>
public static class ErasureFailures
{
    /// <summary>The person is the last active manager of a pharmacy; another member must be made manager there first.</summary>
    public static ApiError LastManager() => new ErasureRefusedLastManager();

    public static ApiError NotFound(Guid id) => new ErasureNotFound(id);

    /// <summary>How the save of an erasure lost a race; it names no pharmacy, since an erasure touches several.</summary>
    public static ApiError LostRace(LostRace race) => race switch
    {
        VersionMovedOn => PharmacyFailures.ConcurrentSave(),
        SaveDeadlocked => PharmacyFailures.ConcurrentSave(),
    };
}
