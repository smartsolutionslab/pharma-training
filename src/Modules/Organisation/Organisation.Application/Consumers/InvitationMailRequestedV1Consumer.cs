using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails an invitation's link once its invite or renewal is committed, from the host's durable local queue
/// (docs/adr/0004). It issues a fresh token at send time, commits its hash, then sends, so a mailed link is never dead
/// on arrival and no token is ever at rest. A failure throws, so Wolverine retries it and then dead-letters it.
/// It does not deduplicate on <c>Metadata.EventIdentifier</c>: a send that fails after the commit must be sent again,
/// so a redelivery mails a second, newer link, and the invitation's state is the key. An invitation that is no longer
/// open or has run out is dropped.
/// </summary>
public sealed partial class InvitationMailRequestedV1Consumer(
    IPharmacyStore pharmacies,
    IOrganisationUnitOfWork unitOfWork,
    IMailSender mails,
    IFrontendLinks links,
    TimeProvider time,
    ILogger<InvitationMailRequestedV1Consumer> logger)
{
    public async Task HandleAsync(InvitationMailRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        var found = await pharmacies.LoadAsync(PharmacyId.From(message.PharmacyId), cancellationToken);
        if (!found.HasValue)
        {
            PharmacyGone(logger, message.MembershipId);
            return;
        }

        var pharmacy = found.Value;
        var membership = MembershipId.From(message.MembershipId);
        var token = InvitationTokens.Next();
        var issued = pharmacy.IssueInvitationLink(membership, token, Timestamp.From(time.GetUtcNow()));
        if (issued.IsFailure)
        {
            if (issued.Error is InvitationExpired)
            {
                Expired(logger, issued.Error.Code, message.MembershipId);
            }
            else
            {
                NotOpen(logger, issued.Error.Code, message.MembershipId);
            }

            return;
        }

        // Composed before the commit, so a missing web app address fails before anything is written.
        var link = links.LinkTo($"/invitations/accept#token={token.Value}");
        var invited = pharmacy.Memberships.Single(m => m.Id == membership);
        var mail = InvitationMails.Compose(pharmacy.Name, invited.Email, issued.Value, link);

        // A lost race throws, so Wolverine retries and the retry reloads the pharmacy; nothing is mailed for it.
        await unitOfWork.CommitAsync(cancellationToken);

        // A revoke (or accept, or end) that committed after this commit killed the link just stored. Read the status
        // again from the store, no-tracking, so the read sees what others committed and not this run's own copy; only
        // a revoke between this read and the server's acceptance of the mail still reaches the invitee (docs/adr/0004).
        if (!await IsStillInvitedAsync(pharmacy.Id, membership, cancellationToken))
        {
            ClosedBeforeSend(logger, message.MembershipId);
            return;
        }

        await mails.SendAsync(mail, cancellationToken);
    }

    private async Task<bool> IsStillInvitedAsync(PharmacyId pharmacy, MembershipId membership, CancellationToken cancellationToken)
    {
        var stored = await pharmacies.FindAsync(pharmacy, cancellationToken);
        return stored.HasValue
            && stored.Value.Memberships.Any(m => m.Id == membership && m.Status == MembershipStatus.Invited);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No pharmacy holds membership {Membership}; its invitation mail is dropped.")]
    private static partial void PharmacyGone(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation mail of membership {Membership} is dropped: {Code}.")]
    private static partial void NotOpen(ILogger logger, string code, Guid membership);

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation of membership {Membership} closed after its link was issued; its mail is dropped.")]
    private static partial void ClosedBeforeSend(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} arrived after the invitation ran out and is dropped: {Code}.")]
    private static partial void Expired(ILogger logger, string code, Guid membership);
}
