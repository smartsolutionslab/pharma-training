using SmartSolutionsLab.PharmaTraining.BuildingBlocks.DomainEvents;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Reactions;

/// <summary>
/// Sends the invitation mail, new or renewed, once the invitation is committed. The token travels only in the link's
/// fragment, which browsers never send to a server. A failed send is not caught: the dispatcher logs it and the
/// invitation stays; the manager renews it to send another (docs/adr/0004).
/// </summary>
public sealed class SendInvitationMail(IMailSender mails, IFrontendLinks links) : IDomainEventReaction<MemberInvited>
{
    public async Task ReactAsync(MemberInvited domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var link = links.LinkTo($"/invitations/accept#token={domainEvent.Token.Value}");
        await mails.SendAsync(InvitationMails.Compose(domainEvent.Name, domainEvent.Email, domainEvent.ExpiresAt, link), cancellationToken);
    }
}
