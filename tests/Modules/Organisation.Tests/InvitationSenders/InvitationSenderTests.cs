using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationSenders;

/// <summary>
/// A signed-in user requests at most so many invitation mails per UTC calendar day (100 by default), across every
/// pharmacy they manage (#265). The tally follows the pharmacy's rule (#158): the counted day is today, or a stored day
/// of tomorrow; a stored day before today or beyond tomorrow (only a hand-edited row, since every instance counts on
/// the database's clock) is discarded and today's tally starts afresh, and is reported so it can be logged. A refusal
/// changes nothing.
/// </summary>
public class InvitationSenderTests
{
    private const string TooManyToday = "organisation.too-many-invitations-sent-today";

    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextMidnight = new(2027, 4, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Sina = PersonId.From(Guid.CreateVersion7());
    private static readonly DailyMailCap CapOfThree = DailyMailCap.From(3);

    [Fact]
    public void The_first_mail_of_a_sender_counts_one_on_its_utc_day()
    {
        var sender = InvitationSender.FirstMailed(Sina, At(Start));

        sender.Id.ShouldBe(Sina);
        sender.MailDay.ShouldBe(UtcDay.Of(At(Start)));
        sender.MailsThatDay.ShouldBe(InvitationMailCount.From(1));
        sender.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_sender_at_the_daily_cap_is_refused_until_the_next_utc_day()
    {
        var sender = InvitationSender.FirstMailed(Sina, At(Start));
        sender.RequestMail(At(Start.AddHours(1)), CapOfThree).IsSuccess.ShouldBeTrue("the second mail of a day capped at three was refused");
        sender.RequestMail(At(Start.AddHours(2)), CapOfThree).IsSuccess.ShouldBeTrue("the third mail of a day capped at three was refused");

        var refused = sender.RequestMail(At(Start.AddHours(3)), CapOfThree);

        refused.IsSuccess.ShouldBeFalse("a fourth mail was requested by a sender capped at three a day");
        refused.Error.Code.ShouldBe(TooManyToday);
        var tooMany = refused.Error.ShouldBeOfType<TooManyInvitationsSentToday>();
        tooMany.Sender.ShouldBe(Sina);
        tooMany.RetryAt.ShouldBe(At(NextMidnight));
        sender.MailsThatDay.Value.ShouldBe(3);
    }

    [Fact]
    public void A_new_utc_day_starts_the_count_afresh()
    {
        var capOfTwo = DailyMailCap.From(2);
        var sender = InvitationSender.FirstMailed(Sina, At(NextMidnight.AddMinutes(-2)));
        sender.RequestMail(At(NextMidnight.AddMinutes(-1)), capOfTwo).IsSuccess.ShouldBeTrue();

        var lastSecond = sender.RequestMail(At(NextMidnight.AddSeconds(-1)), capOfTwo);
        var midnight = sender.RequestMail(At(NextMidnight), capOfTwo);

        lastSecond.IsSuccess.ShouldBeFalse("a third mail was requested at 23:59:59 UTC by a sender capped at two");
        lastSecond.Error.ShouldBeOfType<TooManyInvitationsSentToday>().RetryAt.ShouldBe(At(NextMidnight));
        midnight.IsSuccess.ShouldBeTrue("the next UTC day still counted the day before's mails");
        sender.MailDay.ShouldBe(UtcDay.Of(At(NextMidnight)));
        sender.MailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public void A_stored_day_of_tomorrow_counts_as_today_and_a_day_further_ahead_restarts_the_tally()
    {
        var capOfTwo = DailyMailCap.From(2);
        var tomorrow = InvitationSender.FirstMailed(Sina, At(NextMidnight.AddSeconds(30)));
        var further = InvitationSender.FirstMailed(Sina, At(Start.AddDays(3)));
        further.RequestMail(At(Start.AddDays(3).AddMinutes(1)), capOfTwo).IsSuccess.ShouldBeTrue();

        var behind = tomorrow.RequestMail(At(NextMidnight.AddSeconds(-10)), capOfTwo);
        var third = tomorrow.RequestMail(At(NextMidnight.AddSeconds(-5)), capOfTwo);
        var today = further.RequestMail(At(Start), capOfTwo);

        behind.IsSuccess.ShouldBeTrue("the second mail of a day capped at two was refused");
        tomorrow.MailDay.ShouldBe(UtcDay.Of(At(NextMidnight)), "a clock behind moved the counted day back");
        tomorrow.MailsThatDay.Value.ShouldBe(2, "a clock 40 seconds behind restarted the next day's count");
        third.IsSuccess.ShouldBeFalse("a third mail was requested on a day capped at two from a clock behind");
        third.Error.ShouldBeOfType<TooManyInvitationsSentToday>().RetryAt.ShouldBe(At(NextMidnight.AddDays(1)));
        today.IsSuccess.ShouldBeTrue("a stored day three days ahead with its tally at the cap refused today's first mail");
        further.MailDay.ShouldBe(UtcDay.Of(At(Start)), "a stored day three days ahead was kept as the counted day");
        further.MailsThatDay.Value.ShouldBe(1, "a stored day three days ahead kept its tally");
    }

    [Fact]
    public void A_stored_day_beyond_tomorrow_is_reported_so_the_clock_fault_can_be_logged()
    {
        var ahead = InvitationSender.FirstMailed(Sina, At(Start.AddDays(3)));

        var beyond = ahead.MailDayBeyondTomorrow(At(Start));
        var tomorrow = ahead.MailDayBeyondTomorrow(At(Start.AddDays(2)));
        var today = ahead.MailDayBeyondTomorrow(At(Start.AddDays(3)));

        beyond.HasValue.ShouldBeTrue("a stored day three days ahead was not reported");
        beyond.Value.ShouldBe(UtcDay.Of(At(Start.AddDays(3))));
        tomorrow.HasValue.ShouldBeFalse("a stored day of tomorrow was reported");
        today.HasValue.ShouldBeFalse("a stored day of today was reported");
    }

    [Fact]
    public void A_refused_mail_changes_nothing()
    {
        var capOfOne = DailyMailCap.From(1);
        var sender = InvitationSender.FirstMailed(Sina, At(Start));
        var day = sender.MailDay;
        var count = sender.MailsThatDay;

        var refused = sender.RequestMail(At(Start.AddHours(1)), capOfOne);

        refused.IsSuccess.ShouldBeFalse();
        sender.MailDay.ShouldBe(day);
        sender.MailsThatDay.ShouldBe(count);
        sender.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void The_errors_factory_names_the_sender_and_the_instant()
    {
        var error = InvitationSenderErrors.TooManyToday(Sina, At(NextMidnight));

        var tooMany = error.ShouldBeOfType<TooManyInvitationsSentToday>();
        tooMany.Code.ShouldBe(TooManyToday);
        tooMany.Sender.ShouldBe(Sina);
        tooMany.RetryAt.ShouldBe(At(NextMidnight));
    }

    private static Timestamp At(DateTimeOffset instant) => Timestamp.From(instant);
}
