using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// The time a person worked in a pharmacy (#52): from the instant the membership started on, up to but not including the
/// instant it ended, since on that instant the person no longer works there. Without an ending it has not ended.
/// </summary>
public sealed record MembershipPeriod
{
    private MembershipPeriod(PersonId person, Timestamp startedAt, Timestamp? endedAt)
    {
        Person = person;
        StartedAt = startedAt;
        EndedAt = endedAt;
    }

    public PersonId Person { get; }

    public Timestamp StartedAt { get; }

    public Timestamp? EndedAt { get; }

    public static MembershipPeriod Of(PersonId person, Timestamp startedAt, Timestamp? endedAt)
    {
        Ensure.That(person).IsNotNull();
        Ensure.That(startedAt).IsNotNull();
        return new MembershipPeriod(person, startedAt, endedAt);
    }

    /// <summary>Whether the person was a member at the instant: start inclusive, end exclusive.</summary>
    public bool Covers(Timestamp instant)
    {
        Ensure.That(instant).IsNotNull();
        return StartedAt <= instant && (EndedAt is null || instant < EndedAt);
    }
}
