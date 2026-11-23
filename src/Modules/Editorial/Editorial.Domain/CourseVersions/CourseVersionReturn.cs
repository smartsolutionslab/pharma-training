using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// The record of one return of a course version to draft: who returned it, when and why. Only
/// <see cref="CourseVersion.ReturnToDraft"/> makes one, and once stored it never changes; a later return is a record of
/// its own. It is stored beside the version, not loaded with it (ADR 0013).
/// </summary>
public sealed class CourseVersionReturn : Entity<CourseVersionReturnId>
{
    internal CourseVersionReturn(CourseVersionReturnId id, CourseVersionId version, PersonId returnedBy, Timestamp returnedAt, ReturnReason reason)
        : base(id)
    {
        Version = version;
        ReturnedBy = returnedBy;
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

    public Timestamp ReturnedAt { get; } = default!;

    public ReturnReason Reason { get; } = default!;
}
