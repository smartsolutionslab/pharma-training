namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// Learning's copy of a change Organisation made to a membership, one row per version: it proves the membership has
/// started even when its start has not arrived. Written once per version, by the consumer of <c>MembershipChangedV1</c>.
/// Copied from Compliance's <c>Compliance.Domain/Members/MembershipChange.cs</c>, without name, job function or role.
/// </summary>
public sealed class MembershipChangeCopy
{
    public Guid MembershipId { get; set; }

    /// <summary>The membership's version, 2 or more; with the membership id it is the row's key.</summary>
    public int Version { get; set; }

    public Guid PersonId { get; set; }

    public Guid PharmacyId { get; set; }
}
