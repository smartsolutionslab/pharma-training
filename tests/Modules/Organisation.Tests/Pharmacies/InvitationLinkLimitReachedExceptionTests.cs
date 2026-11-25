using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// At the cap of an invitation's links no retry can succeed before a manager renews the invitation, so the failure is an
/// <see cref="IPermanentMessageFailure"/>, which the host dead-letters on its first attempt (Architecture.Tests asks the
/// host's policy that with a stub of its own; #198's review).
/// </summary>
public class InvitationLinkLimitReachedExceptionTests
{
    [Fact]
    public void A_reached_link_limit_is_a_permanent_message_failure() =>
        new InvitationLinkLimitReachedException(Guid.CreateVersion7()).ShouldBeAssignableTo<IPermanentMessageFailure>();

    [Fact]
    public void A_reached_link_limit_names_the_membership_and_the_code_and_carries_no_inner_exception()
    {
        var membership = Guid.CreateVersion7();

        var failure = new InvitationLinkLimitReachedException(membership);

        failure.Message.ShouldContain(membership.ToString());
        failure.Message.ShouldContain("organisation.invitation-link-limit-reached");
        failure.InnerException.ShouldBeNull();
    }
}
