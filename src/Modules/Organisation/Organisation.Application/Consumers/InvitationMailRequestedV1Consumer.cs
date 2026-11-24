using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// Mails an invitation's link once its invite or renewal is committed, from the host's durable local queue
/// (docs/adr/0004). A version already recorded as mailed is skipped, however often its message is delivered.
/// Otherwise it issues a fresh token, commits its hash beside the version's earlier ones, reads the membership again,
/// sends, and records the version as mailed. So a mailed link is never dead on arrival, no token is ever at rest, and a
/// retry after a send that may have reached the server adds a link without killing the one the invitee may already
/// hold: every link of the version opens the invitation, up to <see cref="Pharmacy.MaxInvitationLinksPerVersion"/>, and
/// a renewal clears them.
/// <para>
/// A re-read or a send that fails takes its link back again, commits that and rethrows the same exception, unless the
/// failure says the mail may be out: only a <see cref="MailNotSentException"/> or <see cref="MailSendTimedOutException"/>
/// whose <c>MayHaveBeenDelivered</c> is <see langword="true"/> does, and it keeps its link; any other exception, the
/// re-read's included, cannot have delivered. Only links whose mails may be out count towards the cap, so an SMTP
/// outage, a refusing server, a missing setting or a database that fails the re-read never reaches it, and Wolverine
/// retries the message and then dead-letters it. At the cap it sends nothing, issues no link and throws
/// <see cref="InvitationLinkLimitReachedException"/>, an <c>IPermanentMessageFailure</c>, so the host dead-letters the
/// message on that first failure, without a retry, and it stays visible until a renewal. Once the send has returned
/// nothing throws: a record that fails, whatever the failure, is logged.
/// </para>
/// <para>
/// It does not deduplicate on <c>Metadata.EventIdentifier</c>: a send that failed must be sent again, and the
/// invitation's version and its mailed mark are the key. An invitation that is no longer open or has run out, on the
/// database's clock that stamped its expiry, is dropped.
/// Two gaps remain, in which both links work and nothing is lost: when the send returned but recording it failed, a
/// redelivery mails the version again; and two runs of one version that overlap (an invite and a quick renewal's
/// message, or a parallel redelivery) can each mail before either records it, each using up one of the cap's links.
/// </para>
/// </summary>
public sealed partial class InvitationMailRequestedV1Consumer(
    IPharmacyStore pharmacies,
    IOrganisationUnitOfWork unitOfWork,
    IMailSender mails,
    IFrontendLinks links,
    IDatabaseClock clock,
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
        var issued = pharmacy.IssueInvitationLink(membership, token, await clock.NowAsync(cancellationToken));
        if (issued.IsFailure)
        {
            switch (issued.Error)
            {
                case InvitationExpired:
                    Expired(logger, issued.Error.Code, message.MembershipId);
                    break;
                case InvitationLinkLimitReached:
                    // Thrown, not dropped: the message dead-letters and stays visible until a renewal clears the links.
                    throw new InvitationLinkLimitReachedException(message.MembershipId);
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

        try
        {
            // A revoke, accept, end or renewal that committed after this commit killed the link just stored. Read the
            // membership again from the store, no-tracking, so the read sees what others committed and not this run's own
            // copy, and send only while it is still invited at this version with this run's hash among its links; only a
            // change between this read and the server's acceptance of the mail still reaches the invitee (docs/adr/0004).
            // The read sits inside the withdraw path: when it throws, nothing was sent, so its link is taken back too.
            if (!await StillHoldsThisLinkAsync(pharmacy.Id, membership, version, InvitationTokenHash.Of(token), cancellationToken))
            {
                ClosedBeforeSend(logger, message.MembershipId);
                return;
            }

            await mails.SendAsync(mail, cancellationToken);
        }
        catch (Exception failed) when (!MayBeOut(failed))
        {
            // Unless the failure says the mail may be out, its link is taken back: it neither works nor counts towards the
            // cap, which only links whose mails may be out reach. A re-read that failed never sent, so it is one of them.
            // Whatever the withdrawal meets, the failure goes on, so Wolverine retries the mail and finally dead-letters it.
            await WithdrawAsync(pharmacy, membership, version, token);
            throw;
        }

        // The mail is out: whatever fails from here on is logged, never thrown, or Wolverine would mail the version again.
        // A save that lost a race or threw leaves the version unmarked, and so does one refused because the invitation
        // changed since.
        try
        {
            if (pharmacy.RecordInvitationMailed(membership, version).IsFailure || !await unitOfWork.TryCommitAsync(cancellationToken))
            {
                MailedButNotRecorded(logger, message.MembershipId, version.Value);
            }
        }
        catch (Exception recording)
        {
            MailedButRecordingFailed(logger, recording, message.MembershipId, version.Value);
        }
    }

    /// <summary>Only a failure that says so may have left the mail with the server; any other cannot have.</summary>
    private static bool MayBeOut(Exception failed) =>
        failed is MailNotSentException { MayHaveBeenDelivered: true } or MailSendTimedOutException { MayHaveBeenDelivered: true };

    /// <summary>
    /// Takes back this run's link and commits that; a withdrawal that fails is logged, never thrown in the send's place.
    /// It runs to its end even when the caller cancelled, which is one of the failures it cleans up after.
    /// </summary>
    private async Task WithdrawAsync(Pharmacy pharmacy, MembershipId membership, InvitationVersion version, InvitationToken token)
    {
        try
        {
            if (pharmacy.WithdrawInvitationLink(membership, token).IsFailure)
            {
                LinkAlreadyGone(logger, membership.Value);
                return;
            }

            if (await unitOfWork.TryCommitAsync(CancellationToken.None))
            {
                return;
            }

            // A renewal, accept or revoke that won the race cleared the links itself; anything else left this one stored.
            if (await MovedOnAsync(pharmacy.Id, membership, version))
            {
                LinkAlreadyGone(logger, membership.Value);
            }
            else
            {
                LinkNotWithdrawn(logger, membership.Value);
            }
        }
        catch (Exception withdrawal)
        {
            LinkWithdrawalFailed(logger, withdrawal, membership.Value);
        }
    }

    /// <summary>Whether the stored invitation is no longer open at this version, so its links are cleared already.</summary>
    private async Task<bool> MovedOnAsync(PharmacyId pharmacy, MembershipId membership, InvitationVersion version)
    {
        var stored = await pharmacies.FindAsync(pharmacy, CancellationToken.None);
        return !stored.HasValue
            || !stored.Value.Memberships.Any(m => m.Id == membership && m.Status == MembershipStatus.Invited && m.InvitationVersion == version);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The invitation mail of membership {Membership} cannot have been delivered, and a renewal, accept or revoke has cleared its link already.")]
    private static partial void LinkAlreadyGone(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} cannot have been delivered, but its link could not be taken back; it counts towards the version's links.")]
    private static partial void LinkNotWithdrawn(ILogger logger, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} cannot have been delivered, and taking its link back failed; it counts towards the version's links.")]
    private static partial void LinkWithdrawalFailed(ILogger logger, Exception exception, Guid membership);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} was sent for version {Version}, but recording it failed; a redelivery mails it again.")]
    private static partial void MailedButNotRecorded(ILogger logger, Guid membership, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation mail of membership {Membership} was sent for version {Version}, but recording it failed; a redelivery mails it again.")]
    private static partial void MailedButRecordingFailed(ILogger logger, Exception exception, Guid membership, int version);
}
