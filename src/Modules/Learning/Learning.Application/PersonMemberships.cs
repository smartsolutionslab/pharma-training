using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// What Learning's copy holds of one person's memberships, and whether that makes them an active member
/// (docs/adr/0017): a membership has started once its start or any of its changes arrived, since Organisation changes
/// only active memberships and every change names person and pharmacy; it has ended once its ending arrived, whatever
/// arrived before or after it, since an ended membership is never reactivated. Decided when read, after the
/// resolve-on-read idea of Compliance's <c>Compliance.Domain/Members/CurrentMembership.cs</c>. Every row must be the
/// person's; one of another person is refused.
/// </summary>
public sealed record PersonMemberships(
    PersonId Person,
    IReadOnlyCollection<MemberCopy> Started,
    IReadOnlyCollection<MembershipChangeCopy> Changed,
    IReadOnlyCollection<MembershipEndingCopy> Ended)
{
    private readonly bool active = Resolve(Person, Started, Changed, Ended);

    /// <summary>Whether at least one of the person's memberships, in any pharmacy, has started and not ended.</summary>
    public bool IsActive => active;

    private static bool Resolve(
        PersonId person,
        IReadOnlyCollection<MemberCopy> started,
        IReadOnlyCollection<MembershipChangeCopy> changed,
        IReadOnlyCollection<MembershipEndingCopy> ended)
    {
        Ensure.That(person).IsNotNull();
        Ensure.That(started).IsNotNull();
        Ensure.That(changed).IsNotNull();
        Ensure.That(ended).IsNotNull();

        HashSet<Guid> open = [];
        foreach (var member in started)
        {
            Ensure.That(member, nameof(started)).IsNotNull();
            OfPerson(person, member.PersonId, nameof(started));
            open.Add(member.Id);
        }

        foreach (var change in changed)
        {
            Ensure.That(change, nameof(changed)).IsNotNull();
            OfPerson(person, change.PersonId, nameof(changed));
            open.Add(change.MembershipId);
        }

        foreach (var ending in ended)
        {
            Ensure.That(ending, nameof(ended)).IsNotNull();
            OfPerson(person, ending.PersonId, nameof(ended));
            open.Remove(ending.Id);
        }

        return open.Count > 0;
    }

    private static void OfPerson(PersonId person, Guid rowPerson, string parameterName) =>
        Ensure.That(rowPerson.ToString(), parameterName).Satisfies(_ => rowPerson == person.Value, $"is a membership of another person than {person}.");
}
