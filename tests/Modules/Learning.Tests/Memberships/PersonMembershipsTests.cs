using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

/// <summary>
/// Whether a person is an active member, by Learning's copy of their memberships (#194, D1): a membership has started
/// once its start or any of its changes arrived, and has ended once its ending arrived, whatever arrived before or after
/// it, since an ended membership is never reactivated. One membership that has started and not ended, in any pharmacy,
/// makes the person an active member.
/// </summary>
public class PersonMembershipsTests
{
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public void A_person_without_memberships_is_not_an_active_member() =>
        new PersonMemberships(Anna, [], [], []).IsActive.ShouldBeFalse();

    [Fact]
    public void A_started_membership_without_an_ending_is_active() =>
        new PersonMemberships(Anna, [MembershipCopyRows.Started(Anna)], [], []).IsActive.ShouldBeTrue();

    [Fact]
    public void A_change_whose_start_has_not_arrived_makes_the_membership_active()
    {
        var notArrived = MembershipCopyRows.Started(Anna);

        new PersonMemberships(Anna, [], [MembershipCopyRows.ChangeOf(notArrived, 2)], []).IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_ending_ends_the_membership_whether_it_arrived_before_or_after_its_start(bool startArrived)
    {
        var member = MembershipCopyRows.Started(Anna);

        var memberships = new PersonMemberships(Anna, startArrived ? [member] : [], [], [MembershipCopyRows.EndingOf(member)]);

        memberships.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_change_arriving_after_the_ending_does_not_reopen_the_membership()
    {
        var member = MembershipCopyRows.Started(Anna);

        var memberships = new PersonMemberships(Anna, [member], [MembershipCopyRows.ChangeOf(member, 2)], [MembershipCopyRows.EndingOf(member)]);

        memberships.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void One_open_membership_beside_an_ended_one_is_active()
    {
        var ended = MembershipCopyRows.Started(Anna);
        var open = MembershipCopyRows.Started(Anna);

        var memberships = new PersonMemberships(Anna, [ended, open], [], [MembershipCopyRows.EndingOf(ended)]);

        memberships.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Two_versions_of_one_change_count_as_one_membership()
    {
        var member = MembershipCopyRows.Started(Anna);

        var memberships = new PersonMemberships(
            Anna,
            [],
            [MembershipCopyRows.ChangeOf(member, 2), MembershipCopyRows.ChangeOf(member, 3)],
            [MembershipCopyRows.EndingOf(member)]);

        memberships.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("start")]
    [InlineData("change")]
    [InlineData("ending")]
    public void A_row_of_another_person_is_refused(string row)
    {
        var someoneElse = MembershipCopyRows.Started(PersonId.From(Guid.CreateVersion7()));
        var own = MembershipCopyRows.Started(Anna);

        // Refused when the rows are put together or, at the latest, when they are asked.
        Should.Throw<GuardException>(() => new PersonMemberships(
            Anna,
            row == "start" ? [own, someoneElse] : [own],
            row == "change" ? [MembershipCopyRows.ChangeOf(someoneElse, 2)] : [],
            row == "ending" ? [MembershipCopyRows.EndingOf(someoneElse)] : []).IsActive);
    }
}
