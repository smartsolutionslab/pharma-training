namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// Learning's copy of a membership Organisation started: whose it is and in which pharmacy, without name, job function
/// or role, which Learning does not use (docs/adr/0017). Written once, by the consumer of <c>MembershipStartedV1</c>,
/// and never changed. Copied from Compliance's <c>Compliance.Domain/Members/Member.cs</c> as a plain read model like
/// <see cref="CourseVersionCopy"/>.
/// </summary>
public sealed class MemberCopy
{
    /// <summary>The membership's id.</summary>
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public Guid PharmacyId { get; set; }
}
