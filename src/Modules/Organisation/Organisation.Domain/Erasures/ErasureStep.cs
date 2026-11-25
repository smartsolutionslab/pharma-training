using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>One module's completed part of an erasure, and when it completed.</summary>
public sealed class ErasureStep
{
    private ErasureStep(ErasureModule module, Timestamp completedAt)
    {
        Module = module;
        CompletedAt = completedAt;
    }

    public ErasureModule Module { get; }

    public Timestamp CompletedAt { get; }

    internal static ErasureStep Completed(ErasureModule module, Timestamp completedAt) => new(module, completedAt);
}
