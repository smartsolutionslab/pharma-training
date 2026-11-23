using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>Compliance's copies of memberships as the consumers leave them, seeded and committed before a test acts.</summary>
internal static class ComplianceMembers
{
    /// <summary>A membership that started in the pharmacy with the role, at version 1.</summary>
    public static async Task<MembershipId> StartAsync(InMemoryMemberStore store, PersonId person, PharmacyId pharmacy, MembershipRole role)
    {
        var membership = MembershipId.From(Guid.CreateVersion7());
        store.Add(Member.Started(membership, person, pharmacy, MemberName.From("Ada Lovelace"), JobFunction.Pta, role));
        await store.CommitAsync(TestContext.Current.CancellationToken);
        return membership;
    }

    /// <summary>A change of the membership to the role, at the version.</summary>
    public static async Task ChangeAsync(InMemoryMemberStore store, MembershipId membership, PersonId person, PharmacyId pharmacy, int version, MembershipRole role)
    {
        store.Add(MembershipChange.Of(membership, person, pharmacy, MembershipVersion.From(version), MemberName.From("Ada Lovelace"), JobFunction.Pta, role));
        await store.CommitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The ending of the membership.</summary>
    public static async Task EndAsync(InMemoryMemberStore store, MembershipId membership, PersonId person, PharmacyId pharmacy)
    {
        store.Add(MembershipEnding.Of(membership, person, pharmacy));
        await store.CommitAsync(TestContext.Current.CancellationToken);
    }
}
