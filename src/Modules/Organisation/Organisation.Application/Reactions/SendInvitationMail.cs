using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Reactions;

/// <summary>
/// Sends the invitation mail once the invitation is committed. Stub for #9's red tests (T8): it sends nothing, and it
/// is not an IDomainEventReaction&lt;MemberInvited&gt; yet, because MemberInvited is still [DeliberatelyUnhandled] and
/// the architecture rule EveryDomainEventHasAReaction refuses an event that is both.
/// </summary>
public sealed class SendInvitationMail(IMailSender mails, IFrontendLinks links)
{
    public Task ReactAsync(MemberInvited domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(mails).IsNotNull();
        Ensure.That(links).IsNotNull();
        return Task.CompletedTask;
    }
}
