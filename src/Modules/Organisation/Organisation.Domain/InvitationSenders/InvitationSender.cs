using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;

/// <summary>
/// A person who requested invitation mails, by their <see cref="PersonId"/>, with the tally of their latest counted
/// UTC day. A sender requests at most <see cref="DailyMailCap"/> mails per UTC day across every pharmacy they manage
/// (docs/adr/0004), counted by the pharmacy's rule (<see cref="UtcDay.CountingDay"/>). It raises no domain event.
/// </summary>
public sealed class InvitationSender : AggregateRoot<PersonId>
{
    private InvitationSender(PersonId id, UtcDay mailDay, InvitationMailCount mailsThatDay)
        : base(id)
    {
        MailDay = mailDay;
        MailsThatDay = mailsThatDay;
    }

    /// <summary>The UTC day of the sender's latest counted invitation mail.</summary>
    public UtcDay MailDay { get; private set; }

    /// <summary>How many invitation mails the sender requested on <see cref="MailDay"/>.</summary>
    public InvitationMailCount MailsThatDay { get; private set; }

    /// <summary>The sender's first invitation mail, counted one on the UTC day of <paramref name="now"/>.</summary>
    public static InvitationSender FirstMailed(PersonId person, Timestamp now)
    {
        Ensure.That(person).IsNotNull();
        Ensure.That(now).IsNotNull();

        return new InvitationSender(person, UtcDay.Of(now), InvitationMailCount.Zero.Next());
    }

    /// <summary>
    /// Counts another invitation mail at <paramref name="now"/>, or refuses it once the counted day's tally has reached
    /// the cap, with the start of the day after it; a refusal changes nothing. A stored day that no longer counts
    /// restarts the tally at one on today.
    /// </summary>
    public Result<Unit, DomainError> RequestMail(Timestamp now, DailyMailCap cap)
    {
        Ensure.That(now).IsNotNull();
        Ensure.That(cap).IsNotNull();

        var counted = UtcDay.CountingDay(now, MailDay);
        if (!counted.HasValue)
        {
            MailDay = UtcDay.Of(now);
            MailsThatDay = InvitationMailCount.Zero.Next();
            return Success(Unit.Value);
        }

        if (MailsThatDay.Value >= cap.Value)
        {
            return Failure(InvitationSenderErrors.TooManyToday(Id, counted.Value.Next().Start));
        }

        MailsThatDay = MailsThatDay.Next();
        return Success(Unit.Value);
    }

    /// <summary>
    /// The stored mail day when it lies beyond tomorrow; none otherwise. With every instance on the database's clock only
    /// a hand-edited row stores one: it is discarded and today's tally starts afresh, and the caller reports it.
    /// </summary>
    public Option<UtcDay> MailDayBeyondTomorrow(Timestamp now)
    {
        Ensure.That(now).IsNotNull();

        return UtcDay.BeyondTomorrow(now, MailDay);
    }
}
