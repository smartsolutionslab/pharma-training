using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Counts one invitation mail on its address and on its sender (docs/adr/0004), after the pharmacy's own checks and in
/// the same commit as the pharmacy: the recipient cooldown first, then the sender's daily cap. Their rows are created
/// when missing and held until the commit in that fixed order, the recipient's before the sender's (#369, #375), so two
/// requests never wait for each other's rows; a wait past the stores' lock timeout is a lost race. A refusal commits
/// nothing: the caller then commits nothing and rolls back at once, with the rows held and any created for it. A sender's
/// stored mail day beyond tomorrow, which only a hand-edited row or a database clock that moved back (a failover or a
/// restore) stores, is logged as a warning naming the person and both days, never the address, and today's tally
/// starts afresh. A recipient's stored instant a cooldown
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
        PharmacyId pharmacy,
        EmailAddress to,
        PersonId by,
        Timestamp now,
        CancellationToken cancellationToken)
    {
        Ensure.That(recipients).IsNotNull();
        Ensure.That(senders).IsNotNull();
        Ensure.That(limits).IsNotNull();
        Ensure.That(logger).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(to).IsNotNull();
        Ensure.That(by).IsNotNull();
        Ensure.That(now).IsNotNull();

        // The recipient's row first, then the sender's: every request holds them in this order (docs/adr/0004), so a
        // request never holds one while waiting for the other's holder. Each is created when missing and held until the
        // commit, so two requests mailing one address or counting on one sender at once count one after the other, the
        // very first two included (#369, #375).
        var loadedRecipient = await recipients.LoadForMailAsync(to, cancellationToken);
        if (loadedRecipient.IsFailure)
        {
            return Failure(PharmacyFailures.LostRace(loadedRecipient.Error, pharmacy.Value));
        }

        var recipient = loadedRecipient.Value;
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

        var loadedSender = await senders.LoadForMailAsync(by, now, cancellationToken);
        if (loadedSender.IsFailure)
        {
            return Failure(PharmacyFailures.LostRace(loadedSender.Error, pharmacy.Value));
        }

        var sender = loadedSender.Value;
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

        return Success(Unit.Value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A recipient stored the invitation mail time {LastMailedAt:O}, a cooldown or more beyond now {Now:O}; it is not trusted and the address may be mailed. The row was edited by hand, or the database's clock moved back (a failover or a restore).")]
    private static partial void LogRecipientMailedAhead(ILogger logger, DateTimeOffset lastMailedAt, DateTimeOffset now);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sender {Person} stored the invitation mail day {StoredDay:yyyy-MM-dd}, beyond tomorrow of today {Today:yyyy-MM-dd}; it is discarded and today's tally starts afresh. The row was edited by hand, or the database's clock moved back (a failover or a restore).")]
    private static partial void LogSenderDayAhead(ILogger logger, Guid person, DateOnly storedDay, DateOnly today);
}
