namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// Learning's copy of the ending of a membership, kept whether its start has arrived or not: an ended membership is
/// never reactivated. Written once, by the consumer of <c>MembershipEndedV1</c>. Copied from Compliance's
/// <c>Compliance.Domain/Members/MembershipEnding.cs</c>.
/// </summary>
public sealed class MembershipEndingCopy
{
    /// <summary>The ended membership's id.</summary>
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public Guid PharmacyId { get; set; }
}
