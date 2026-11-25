using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The record of one return of a course version to draft: who returned it, under which name, when and why. Only
/// <see cref="CourseVersion.ReturnToDraft"/> makes one, and once stored it never changes; a later return is a record of
/// its own. It is stored beside the version, not loaded with it (ADR 0013).
/// </summary>
public sealed class CourseVersionReturn : Entity<CourseVersionReturnId>
{
    internal CourseVersionReturn(CourseVersionReturnId id, CourseVersionId version, PersonId returnedBy, ReviewerName? returnedByName, Timestamp returnedAt, ReturnReason reason)
        : base(id)
    {
        Version = version;
        ReturnedBy = returnedBy;
        ReturnedByName = returnedByName;
        ReturnedAt = returnedAt;
        Reason = reason;
    }

    /// <summary>For EF Core, which sets the rest through the backing fields.</summary>
    private CourseVersionReturn(CourseVersionReturnId id)
        : base(id)
    {
    }

    public CourseVersionId Version { get; } = default!;

    public PersonId ReturnedBy { get; } = default!;

    /// <summary>
    /// The returning editor's display name as the token carried it at return time (#220), a historical record; null on
    /// returns stored before names were kept, and for an editor who is erased (#115).
    /// </summary>
    public ReviewerName? ReturnedByName { get; }

    public Timestamp ReturnedAt { get; } = default!;

    public ReturnReason Reason { get; } = default!;
}
