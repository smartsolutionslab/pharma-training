using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// Who may do what in a pharmacy, decided from Compliance's own copy of the caller's membership there and its current
/// role (docs/adr/0003): someone without a membership that has not ended learns nothing about the pharmacy; a member
/// whose current role lacks the capability is told so.
/// </summary>
public class ComplianceAccessTests
{
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);

        var refused = await ComplianceAccess.RequireAsync(store, PersonId.From(Guid.CreateVersion7()), Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_manager_of_another_pharmacy_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Anna, PharmacyId.From(Guid.CreateVersion7()), MembershipRole.Manager);

        var refused = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_whose_membership_ended_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.EndAsync(store, membership, Anna, Pharmacy);

        var refused = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("compliance.pharmacy-not-found");
        refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_employee_lacks_the_capability()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Employee);

        var refused = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("compliance.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_manager_is_granted()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);

        var granted = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        granted.IsSuccess.ShouldBeTrue();
        granted.Value.Membership.ShouldBe(membership);
        granted.Value.Person.ShouldBe(Anna);
        granted.Value.Pharmacy.ShouldBe(Pharmacy);
    }

    [Fact]
    public async Task A_demoted_manager_lacks_the_capability()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.ChangeAsync(store, membership, Anna, Pharmacy, 2, MembershipRole.Employee);

        var refused = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("compliance.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_promoted_employee_is_granted()
    {
        var store = new InMemoryMemberStore();
        var membership = await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Employee);
        await ComplianceMembers.ChangeAsync(store, membership, Anna, Pharmacy, 2, MembershipRole.Manager);

        var granted = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        granted.IsSuccess.ShouldBeTrue();
        granted.Value.Role.ShouldBe(MembershipRole.Manager);
        granted.Value.Version.ShouldBe(MembershipVersion.From(2));
    }

    /// <summary>
    /// Organisation holds one active membership per person and pharmacy, so two in Compliance's copy mean an ending is
    /// still on its way (or dead-lettered), and Compliance cannot tell which one ended. It fails closed: the capability
    /// holds only when every one of them grants it, so a manager who ended and came back as an employee cannot assign.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Of_two_memberships_one_lacking_the_capability_withholds_it(bool managerFirst)
    {
        var store = new InMemoryMemberStore();
        if (managerFirst)
        {
            await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
            await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Employee);
        }
        else
        {
            await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Employee);
            await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
        }

        var granted = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        granted.IsFailure.ShouldBeTrue("a membership that lacks the capability was outvoted by another one");
        granted.Error.Code.ShouldBe("compliance.capability-missing");
        granted.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Two_memberships_that_both_grant_the_capability_grant_it()
    {
        var store = new InMemoryMemberStore();
        await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);

        var granted = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        granted.IsSuccess.ShouldBeTrue();
        granted.Value.Role.ShouldBe(MembershipRole.Manager);
    }

    [Fact]
    public async Task An_ended_manager_membership_beside_an_active_employee_one_lacks_the_capability()
    {
        var store = new InMemoryMemberStore();
        var ended = await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Manager);
        await ComplianceMembers.EndAsync(store, ended, Anna, Pharmacy);
        await ComplianceMembers.StartAsync(store, Anna, Pharmacy, MembershipRole.Employee);

        var refused = await ComplianceAccess.RequireAsync(store, Anna, Pharmacy, Capability.AssignCourses, TestContext.Current.CancellationToken);

        refused.Error.Code.ShouldBe("compliance.capability-missing");
    }
}
