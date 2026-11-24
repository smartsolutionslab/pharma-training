using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// Who may learn, decided by Learning's own copy of the memberships, not by a realm role (docs/adr/0003,
/// docs/adr/0017): an active member of any pharmacy, since a session is the person's and not a pharmacy's
/// (docs/adr/0010). Anyone else is told that they are no active member. Learning's counterpart of Compliance's
/// <c>Compliance.Application/ComplianceAccess.cs</c>.
/// </summary>
public static class LearnerAccess
{
    public static async Task<Result<Unit, ApiError>> RequireActiveMemberAsync(
        IMembershipCopies memberships,
        PersonId person,
        CancellationToken cancellationToken)
    {
        Ensure.That(memberships).IsNotNull();
        Ensure.That(person).IsNotNull();

        var held = await memberships.OfPersonAsync(person, cancellationToken);
        if (!held.IsActive)
        {
            return Failure(LearningSessionFailures.MembershipMissing());
        }

        return Success(Unit.Value);
    }
}
