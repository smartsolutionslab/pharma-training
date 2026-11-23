using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails an invitation's link once its invite or renewal is committed, from the host's durable local queue
/// (docs/adr/0004). Each invitation version is mailed once: a version already recorded as mailed is skipped, however
/// often its message is delivered. Otherwise it issues a fresh token, commits its hash beside the version's earlier ones,
/// reads the membership again, sends, and records the version as mailed. So a mailed link is never dead on arrival, no
/// token is ever at rest, and a retry after a send that failed adds a link without killing the one the server may
/// already have taken: every link of the version opens the invitation, up to
/// <see cref="Pharmacy.MaxInvitationLinksPerVersion"/>, and a renewal clears them. A send that failed before the server
/// could have taken the mail (<see cref="MailNotSentException.MayHaveBeenDelivered"/> false) takes its link back again,
/// so only links whose mails may be out count towards that cap and an SMTP outage never reaches it. A failure up to and
/// including the send throws, so Wolverine retries it and then dead-letters it.
/// It does not deduplicate on <c>Metadata.EventIdentifier</c>: a send that failed must be sent again, and the
/// invitation's version and its mailed mark are the key. An invitation that is no longer open or has run out is dropped,
/// and so is one whose version holds the most links, without a throw, since a retry could never mail it.
/// One gap remains: when the send returned but recording it failed, the message is mailed again and both links work.
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
        if (pharmacy.Memberships.SingleOrDefault(m => m.Id == membership) is { Status: MembershipStatus.Invited } open
            && open.InvitationMailedFor == open.InvitationVersion)
        {
            AlreadyMailed(logger, message.MembershipId, open.InvitationVersion.Value);
            return;
        }

        var token = InvitationTokens.Next();
        var issued = pharmacy.IssueInvitationLink(membership, token, Timestamp.From(time.GetUtcNow()));
        if (issued.IsFailure)
        {
            switch (issued.Error)
            {
                case InvitationExpired:
                    Expired(logger, issued.Error.Code, message.MembershipId);
                    break;
                case InvitationLinkLimitReached:
                    LinkLimitReached(logger, issued.Error.Code, message.MembershipId, Pharmacy.MaxInvitationLinksPerVersion);
                    break;
                default:
                    NotOpen(logger, issued.Error.Code, message.MembershipId);
                    break;
            }

            return;
        }

        // Composed before the commit, so a missing web app address fails before anything is written.
        var link = links.LinkTo($"/invitations/accept#token={token.Value}");
        var invited = pharmacy.Memberships.Single(m => m.Id == membership);
        var version = invited.InvitationVersion;
        var mail = InvitationMails.Compose(pharmacy.Name, invited.Email, issued.Value, link);

        // A lost race throws, so Wolverine retries and the retry reloads the pharmacy; nothing is mailed for it.
        await unitOfWork.CommitAsync(cancellationToken);

        // A revoke, accept, end or renewal that committed after this commit killed the link just stored. Read the
        // membership again from the store, no-tracking, so the read sees what others committed and not this run's own
        // copy, and send only while it is still invited at this version with this run's hash among its links; only a
        // change between this read and the server's acceptance of the mail still reaches the invitee (docs/adr/0004).
        if (!await StillHoldsThisLinkAsync(pharmacy.Id, membership, version, InvitationTokenHash.Of(token), cancellationToken))
        {
            ClosedBeforeSend(logger, message.MembershipId);
            return;
        }

        try
        {
            await mails.SendAsync(mail, cancellationToken);
        }
        catch (Exception failed) when (CannotHaveBeenDelivered(failed))
        {
            // The server never took this mail, so its link is taken back: it neither works nor counts towards the cap,
            // which only links whose mails may be out reach. Whatever the withdrawal meets, the send's failure goes on,
            // so Wolverine retries the mail and finally dead-letters it.
            await WithdrawAsync(pharmacy, membership, token, cancellationToken);
            throw;
        }

        // The mail is out: a failure from here on must not throw, or Wolverine would mail the version again. A save that
        // lost a race leaves the version unmarked, and so does one refused because the invitation changed since.
        if (pharmacy.RecordInvitationMailed(membership, version).IsFailure || !await unitOfWork.TryCommitAsync(cancellationToken))
        {
            MailedButNotRecorded(logger, message.MembershipId, version.Value);
        }
    }

    private static bool CannotHaveBeenDelivered(Exception failed) =>
        failed is MailNotSentException { MayHaveBeenDelivered: false } or MailSendTimedOutException { MayHaveBeenDelivered: false };

    /// <summary>Takes back this run's link and commits that; a withdrawal that fails is logged, never thrown in the send's place.</summary>
    private async Task WithdrawAsync(Pharmacy pharmacy, MembershipId membership, InvitationToken token, CancellationToken cancellationToken)
    {
        try
        {
            if (pharmacy.WithdrawInvitationLink(membership, token).IsFailure || !await unitOfWork.TryCommitAsync(cancellationToken))
            {
                LinkNotWithdrawn(logger, membership.Value);
            }
        }
        catch (Exception withdrawal)
        {
            LinkWithdrawalFailed(logger, withdrawal, membership.Value);
        }
    }

    private async Task<bool> StillHoldsThisLinkAsync(PharmacyId pharmacy, MembershipId membership, InvitationVersion version, InvitationTokenHash issued, CancellationToken cancellationToken)
    {
        var stored = await pharmacies.FindAsync(pharmacy, cancellationToken);
        return stored.HasValue
            && stored.Value.Memberships.Any(m => m.Id == membership
                && m.Status == MembershipStatus.Invited
                && m.InvitationVersion == version
                && m.InvitationLinks.Any(link => link.Hash == issued));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No pharmacy holds membership {Membership}; its invitation mail is dropped.")]
    private static partial void PharmacyGone(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation of membership {Membership} was mailed for version {Version} already; its mail is not sent again.")]
    private static partial void AlreadyMailed(ILogger logger, Guid membership, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation mail of membership {Membership} is dropped: {Code}.")]
    private static partial void NotOpen(ILogger logger, string code, Guid membership);

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation of membership {Membership} closed or was renewed after its link was issued; its mail is dropped.")]
    private static partial void ClosedBeforeSend(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} arrived after the invitation ran out and is dropped: {Code}.")]
    private static partial void Expired(ILogger logger, string code, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation of membership {Membership} holds {Limit} links for its version already; its mail is dropped and not retried: {Code}.")]
    private static partial void LinkLimitReached(ILogger logger, string code, Guid membership, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} never reached the mail server, and its link could not be taken back; it counts towards the version's links.")]
    private static partial void LinkNotWithdrawn(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} never reached the mail server, and taking its link back failed; it counts towards the version's links.")]
    private static partial void LinkWithdrawalFailed(ILogger logger, Exception exception, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} was sent for version {Version}, but recording it failed; a redelivery mails it again.")]
    private static partial void MailedButNotRecorded(ILogger logger, Guid membership, int version);
}
