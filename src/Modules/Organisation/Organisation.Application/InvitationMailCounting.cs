using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Counts one invitation mail on its address and on its sender (docs/adr/0004), after the pharmacy's own checks and in
/// the same commit as the pharmacy: the recipient cooldown first, then the sender's daily cap. A refusal adds nothing
/// to the unit of work; the caller then commits nothing. A sender's stored mail day beyond tomorrow, which only a
/// hand-edited row or a database clock that moved back (a failover or a restore) stores, is logged as a warning naming
/// the person and both days, never the address, and today's tally starts afresh. A recipient's stored instant a cooldown
/// or more ahead is logged as a warning naming only the two instants, neither the address nor its recipient id (a
/// pseudonym anyone who knows the address can recompute, #326), and the address may be mailed.
/// </summary>
public static partial class InvitationMailCounting
{
    public static async Task<Result<Unit, ApiError>> CountAsync(
        IInvitationRecipientStore recipients,
        IInvitationSenderStore senders,
        RecipientAndSenderMailLimits limits,
        ILogger logger,
        EmailAddress to,
        PersonId by,
        Timestamp now,
        CancellationToken cancellationToken)
    {
        Ensure.That(recipients).IsNotNull();
        Ensure.That(senders).IsNotNull();
        Ensure.That(limits).IsNotNull();
        Ensure.That(logger).IsNotNull();
        Ensure.That(to).IsNotNull();
        Ensure.That(by).IsNotNull();
        Ensure.That(now).IsNotNull();

        var storedRecipient = await recipients.LoadAsync(InvitationRecipientId.For(to), cancellationToken);
        if (storedRecipient.HasValue)
        {
            var recipient = storedRecipient.Value;
            var mailedAhead = recipient.MailedBeyond(now, limits.RecipientCooldown);
            if (mailedAhead.HasValue)
            {
                LogRecipientMailedAhead(logger, mailedAhead.Value.Value, now.Value);
            }

            var mailed = recipient.RequestMail(now, limits.RecipientCooldown);
            if (mailed.IsFailure)
            {
                return Failure(PharmacyFailures.From(mailed.Error));
            }
        }

        // The sender's row is created when missing and held until the commit, so a sender's concurrent mails, their very
        // first two included, count one after the other on one row (#369). A refusal commits nothing, so a row created
        // for it is discarded with the unit of work.
        var sender = await senders.LoadForMailAsync(by, now, cancellationToken);
        var ahead = sender.MailDayBeyondTomorrow(now);
        if (ahead.HasValue)
        {
            LogSenderDayAhead(logger, sender.Id.Value, ahead.Value.Value, UtcDay.Of(now).Value);
        }

        var sent = sender.RequestMail(now, limits.MailsPerSenderPerDay);
        if (sent.IsFailure)
        {
            return Failure(PharmacyFailures.From(sent.Error));
        }

        // A new recipient is added only once both rules have passed, so a refusal leaves nothing to be saved.
        if (!storedRecipient.HasValue)
        {
            recipients.Add(InvitationRecipient.FirstMailed(to, now));
        }

        return Success(Unit.Value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A recipient stored the invitation mail time {LastMailedAt:O}, a cooldown or more beyond now {Now:O}; it is not trusted and the address may be mailed. The row was edited by hand, or the database's clock moved back (a failover or a restore).")]
    private static partial void LogRecipientMailedAhead(ILogger logger, DateTimeOffset lastMailedAt, DateTimeOffset now);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sender {Person} stored the invitation mail day {StoredDay:yyyy-MM-dd}, beyond tomorrow of today {Today:yyyy-MM-dd}; it is discarded and today's tally starts afresh. The row was edited by hand, or the database's clock moved back (a failover or a restore).")]
    private static partial void LogSenderDayAhead(ILogger logger, Guid person, DateOnly storedDay, DateOnly today);
}
