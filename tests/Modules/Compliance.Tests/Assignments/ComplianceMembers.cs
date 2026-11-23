using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>Compliance's copies of memberships as the consumers leave them, seeded and committed before a test acts.</summary>
internal static class ComplianceMembers
{
    private const string DefaultName = "Ada Lovelace";

    /// <summary>A membership that started in the pharmacy with the role, name and job function, at version 1.</summary>
    public static Task<MembershipId> StartAsync(
        InMemoryMemberStore store,
        PersonId person,
        PharmacyId pharmacy,
        MembershipRole role,
        string name = DefaultName,
        JobFunction jobFunction = JobFunction.Pta) =>
        StartAsync(store, MembershipId.From(Guid.CreateVersion7()), person, pharmacy, role, name, jobFunction);

    /// <summary>The membership with this id that started in the pharmacy with the role, name and job function, at version 1.</summary>
    public static async Task<MembershipId> StartAsync(
        InMemoryMemberStore store,
        MembershipId membership,
        PersonId person,
        PharmacyId pharmacy,
        MembershipRole role,
        string name = DefaultName,
        JobFunction jobFunction = JobFunction.Pta)
    {
        store.Add(Member.Started(membership, person, pharmacy, MemberName.From(name), jobFunction, role));
        await store.CommitAsync(TestContext.Current.CancellationToken);
        return membership;
    }

    /// <summary>A change of the membership to the role, name and job function, at the version.</summary>
    public static async Task ChangeAsync(
        InMemoryMemberStore store,
        MembershipId membership,
        PersonId person,
        PharmacyId pharmacy,
        int version,
        MembershipRole role,
        string name = DefaultName,
        JobFunction jobFunction = JobFunction.Pta)
    {
        store.Add(MembershipChange.Of(membership, person, pharmacy, MembershipVersion.From(version), MemberName.From(name), jobFunction, role));
        await store.CommitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The ending of the membership.</summary>
    public static async Task EndAsync(InMemoryMemberStore store, MembershipId membership, PersonId person, PharmacyId pharmacy)
    {
        store.Add(MembershipEnding.Of(membership, person, pharmacy));
        await store.CommitAsync(TestContext.Current.CancellationToken);
    }
}
