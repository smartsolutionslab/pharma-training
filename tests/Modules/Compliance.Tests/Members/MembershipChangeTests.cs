using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MembershipChangeTests
{
    [Fact]
    public void A_change_at_version_one_is_refused() =>
        Should.Throw<GuardException>(() => MembershipChange.Of(
            MembershipId.From(Guid.CreateVersion7()),
            PersonId.From(Guid.CreateVersion7()),
            PharmacyId.From(Guid.CreateVersion7()),
            MembershipVersion.First,
            MemberName.From("Ada Lovelace"),
            JobFunction.Pta,
            MembershipRole.Manager));
}
