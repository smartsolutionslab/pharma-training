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

    /// <summary>The address's first invitation mail, counted at <paramref name="now"/>.</summary>
    public static InvitationRecipient FirstMailed(EmailAddress address, Timestamp now)
    {
        Ensure.That(address).IsNotNull();
        Ensure.That(now).IsNotNull();

        return new InvitationRecipient(InvitationRecipientId.For(address), now);
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

        var retryAt = LastMailedAt.Plus(cooldown.Value);
        if (now < retryAt && LastMailedAt < now.Plus(cooldown.Value))
        {
            return Failure(InvitationRecipientErrors.MailedRecently(Id, retryAt));
        }

        LastMailedAt = now;
        return Success(Unit.Value);
    }
}
