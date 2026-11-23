using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class CurrentMembershipTests
{
    private static readonly MembershipId Membership = MembershipId.From(Guid.CreateVersion7());
    private static readonly PersonId Person = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());

    [Fact]
    public void A_member_without_changes_is_as_it_started_at_version_one()
    {
        var member = Started(MembershipRole.Employee);

        var current = CurrentMembership.Of(member, []);

        current.Membership.ShouldBe(Membership);
        current.Person.ShouldBe(Person);
        current.Pharmacy.ShouldBe(Pharmacy);
        current.Name.ShouldBe(MemberName.From("Ada Lovelace"));
        current.JobFunction.ShouldBe(JobFunction.Pta);
        current.Role.ShouldBe(MembershipRole.Employee);
        current.Version.ShouldBe(MembershipVersion.First);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_change_with_the_highest_version_wins_whatever_order_they_arrived_in(bool newerFirst)
    {
        var promoted = Change(2, MembershipRole.Manager);
        var demoted = Change(3, MembershipRole.Employee);
        IReadOnlyCollection<MembershipChange> arrived = newerFirst ? [demoted, promoted] : [promoted, demoted];

        var current = CurrentMembership.Of(Started(MembershipRole.Employee), arrived);

        current.Role.ShouldBe(MembershipRole.Employee);
        current.Version.ShouldBe(MembershipVersion.From(3));
    }

    [Fact]
    public void An_older_change_arriving_after_a_newer_one_changes_nothing()
    {
        var newer = Change(3, MembershipRole.Manager, JobFunction.Pharmacist, "Ada Byron");
        var older = Change(2, MembershipRole.Employee, JobFunction.Pka, "Ada King");

        var current = CurrentMembership.Of(Started(MembershipRole.Employee), [newer, older]);

        current.Role.ShouldBe(MembershipRole.Manager);
        current.JobFunction.ShouldBe(JobFunction.Pharmacist);
        current.Name.ShouldBe(MemberName.From("Ada Byron"));
        current.Version.ShouldBe(MembershipVersion.From(3));
    }

    [Fact]
    public void A_change_takes_its_name_job_function_and_role_but_keeps_the_members_person_and_pharmacy()
    {
        var change = MembershipChange.Of(
            Membership,
            PersonId.From(Guid.CreateVersion7()),
            PharmacyId.From(Guid.CreateVersion7()),
            MembershipVersion.From(2),
            MemberName.From("Ada Byron"),
            JobFunction.Pka,
            MembershipRole.Manager);

        var current = CurrentMembership.Of(Started(MembershipRole.Employee), [change]);

        current.Membership.ShouldBe(Membership);
        current.Person.ShouldBe(Person);
        current.Pharmacy.ShouldBe(Pharmacy);
        current.Name.ShouldBe(MemberName.From("Ada Byron"));
        current.JobFunction.ShouldBe(JobFunction.Pka);
        current.Role.ShouldBe(MembershipRole.Manager);
        current.Version.ShouldBe(MembershipVersion.From(2));
    }

    [Fact]
    public void A_change_of_another_membership_is_refused()
    {
        var other = MembershipChange.Of(
            MembershipId.From(Guid.CreateVersion7()),
            Person,
            Pharmacy,
            MembershipVersion.From(2),
            MemberName.From("Ada Lovelace"),
            JobFunction.Pta,
            MembershipRole.Manager);

        Should.Throw<GuardException>(() => CurrentMembership.Of(Started(MembershipRole.Employee), [other]));
    }

    [Theory]
    [InlineData(MembershipRole.Manager, true)]
    [InlineData(MembershipRole.Employee, false)]
    public void Only_a_manager_may_assign_courses(MembershipRole role, bool granted)
    {
        role.Grants(Capability.AssignCourses).ShouldBe(granted);
        CurrentMembership.Of(Started(role), []).Grants(Capability.AssignCourses).ShouldBe(granted);
    }

    [Fact]
    public void A_demoted_manager_may_no_longer_assign_courses() =>
        CurrentMembership.Of(Started(MembershipRole.Manager), [Change(2, MembershipRole.Employee)])
            .Grants(Capability.AssignCourses).ShouldBeFalse();

    [Fact]
    public void A_promoted_employee_may_assign_courses() =>
        CurrentMembership.Of(Started(MembershipRole.Employee), [Change(2, MembershipRole.Manager)])
            .Grants(Capability.AssignCourses).ShouldBeTrue();

    private static Member Started(MembershipRole role) =>
        Member.Started(Membership, Person, Pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pta, role);

    private static MembershipChange Change(int version, MembershipRole role, JobFunction jobFunction = JobFunction.Pta, string name = "Ada Lovelace") =>
        MembershipChange.Of(Membership, Person, Pharmacy, MembershipVersion.From(version), MemberName.From(name), jobFunction, role);
}
