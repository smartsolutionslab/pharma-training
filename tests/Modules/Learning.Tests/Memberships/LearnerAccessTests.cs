using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

/// <summary>
/// Who may learn, by Learning's copy of the memberships (#194, D2): an active member of any pharmacy is let through;
/// anyone else is told, with <c>learning.membership-missing</c> and 403, that they are no active member.
/// </summary>
public class LearnerAccessTests
{
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryMembershipCopies memberships = new();

    [Fact]
    public async Task An_active_member_is_let_through()
    {
        memberships.Hold(MembershipCopyRows.Started(Anna));

        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, Anna, TestContext.Current.CancellationToken);

        access.IsSuccess.ShouldBeTrue();
        access.Value.ShouldBe(Unit.Value);
    }

    [Fact]
    public async Task A_person_without_a_membership_is_refused_with_learning_membership_missing_and_403()
    {
        memberships.Hold(MembershipCopyRows.Started(PersonId.From(Guid.CreateVersion7())));

        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, Anna, TestContext.Current.CancellationToken);

        access.IsSuccess.ShouldBeFalse();
        access.Error.ShouldBe(LearningSessionFailures.MembershipMissing());
        access.Error.Code.ShouldBe("learning.membership-missing");
        access.Error.Message.ShouldBe("You are not an active member of any pharmacy; a membership that just started may not have reached Learning yet.");
        access.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_person_whose_only_membership_ended_is_refused()
    {
        var member = MembershipCopyRows.Started(Anna);
        memberships.Hold(member);
        memberships.Hold(MembershipCopyRows.EndingOf(member));

        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, Anna, TestContext.Current.CancellationToken);

        access.IsSuccess.ShouldBeFalse();
        access.Error.Code.ShouldBe("learning.membership-missing");
        access.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_person_with_a_membership_in_another_pharmacy_beside_an_ended_one_is_let_through()
    {
        var ended = MembershipCopyRows.Started(Anna);
        memberships.Hold(ended);
        memberships.Hold(MembershipCopyRows.EndingOf(ended));
        memberships.Hold(MembershipCopyRows.Started(Anna));

        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, Anna, TestContext.Current.CancellationToken);

        access.IsSuccess.ShouldBeTrue();
    }
}
