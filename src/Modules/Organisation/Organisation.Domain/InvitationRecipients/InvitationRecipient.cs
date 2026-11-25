using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>
/// An address that was mailed an invitation, by its derived id (the address itself is not stored), with the instant of
/// its latest invitation mail. An address is mailed at most once per <see cref="RecipientCooldown"/>, whichever
/// pharmacy, membership or invitation version the mail is for (docs/adr/0004). It raises no domain event.
/// </summary>
public sealed class InvitationRecipient : AggregateRoot<InvitationRecipientId>
{
    private InvitationRecipient(InvitationRecipientId id, Timestamp lastMailedAt)
        : base(id) => LastMailedAt = lastMailedAt;

    /// <summary>The instant of the address's latest counted invitation mail.</summary>
    public Timestamp LastMailedAt { get; private set; }

    /// <summary>
    /// An address with no mail counted yet: its latest mail at the Unix epoch, long past any cooldown, so its first mail
    /// is counted with <see cref="RequestMail"/> like every other. It is the row the store creates when an address has
    /// none, and is committed only once a mail has been counted on it (#375).
    /// </summary>
    public static InvitationRecipient NotYetMailed(EmailAddress address)
    {
        Ensure.That(address).IsNotNull();

        return new InvitationRecipient(InvitationRecipientId.For(address), Timestamp.From(DateTimeOffset.UnixEpoch));
    }

    /// <summary>
    /// Counts another invitation mail at <paramref name="now"/>, or refuses it within the cooldown with the instant
    /// the address may be mailed again; a refusal changes nothing. A stored instant less than a cooldown ahead of
    /// <paramref name="now"/> is trusted; one a cooldown or more ahead is not, and is replaced by <paramref name="now"/>,
    /// so no faulty instant locks an address out for more than two cooldowns.
    /// </summary>
    public Result<Unit, DomainError> RequestMail(Timestamp now, RecipientCooldown cooldown)
    {
        Ensure.That(now).IsNotNull();
        Ensure.That(cooldown).IsNotNull();

        // The ceiling is checked before anything is added to the stored instant, so one near the end of the calendar is
        // distrusted instead of overflowing.
        if (LastMailedAt < now.Plus(cooldown.Value))
        {
            var retryAt = LastMailedAt.Plus(cooldown.Value);
            if (now < retryAt)
            {
                return Failure(InvitationRecipientErrors.MailedRecently(Id, retryAt, now));
            }
        }

        LastMailedAt = now;
        return Success(Unit.Value);
    }

    /// <summary>
    /// The stored instant when it lies a cooldown or more beyond <paramref name="now"/>; none otherwise. With every
    /// instance on the database's clock only a hand-edited row, or a database clock that moved back (a failover or a
    /// restore), stores one: it is not trusted and is replaced by <paramref name="now"/>, and the caller reports it.
    /// </summary>
    public Option<Timestamp> MailedBeyond(Timestamp now, RecipientCooldown cooldown)
    {
        Ensure.That(now).IsNotNull();
        Ensure.That(cooldown).IsNotNull();

        if (LastMailedAt >= now.Plus(cooldown.Value))
        {
            return Option<Timestamp>.Some(LastMailedAt);
        }

        return Option<Timestamp>.None;
    }
}
