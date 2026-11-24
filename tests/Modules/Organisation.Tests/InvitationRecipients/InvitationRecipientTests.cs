using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationRecipients;

/// <summary>
/// An address is mailed an invitation at most once per cooldown (10 minutes by default), whichever pharmacy,
/// membership or invitation version the mail is for (#265). A refusal names the instant the address may be mailed
/// again and changes nothing. A stored instant less than a cooldown ahead is trusted; one a cooldown or more ahead is
/// not, so no faulty instant locks an address out for more than two cooldowns.
/// </summary>
public class InvitationRecipientTests
{
    private const string MailedRecently = "organisation.recipient-mailed-recently";

    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");
    private static readonly RecipientCooldown TenMinutes = RecipientCooldown.From(TimeSpan.FromMinutes(10));

    [Fact]
    public void The_first_mail_to_an_address_is_counted_at_once()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));

        recipient.Id.ShouldBe(InvitationRecipientId.For(Anna));
        recipient.LastMailedAt.ShouldBe(At(Start));
        recipient.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_mail_within_the_cooldown_is_refused_with_the_instant_the_address_may_be_mailed_again()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));

        var refused = recipient.RequestMail(At(Start.AddMinutes(10).AddSeconds(-1)), TenMinutes);

        refused.IsSuccess.ShouldBeFalse("an address was mailed again 9:59 minutes after its last mail");
        refused.Error.Code.ShouldBe(MailedRecently);
        var recently = refused.Error.ShouldBeOfType<RecipientMailedRecently>();
        recently.Recipient.ShouldBe(InvitationRecipientId.For(Anna));
        recently.RetryAt.ShouldBe(At(Start.AddMinutes(10)));
        refused.Error.Message.ShouldNotContain(Anna.Value);
    }

    [Fact]
    public void A_mail_once_the_cooldown_has_passed_is_counted_and_starts_a_new_cooldown()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));

        var mailed = recipient.RequestMail(At(Start.AddMinutes(10)), TenMinutes);
        var lastMailedAt = recipient.LastMailedAt;
        var refused = recipient.RequestMail(At(Start.AddMinutes(20).AddSeconds(-1)), TenMinutes);

        mailed.IsSuccess.ShouldBeTrue("an address was refused a mail 10 minutes after its last one");
        lastMailedAt.ShouldBe(At(Start.AddMinutes(10)));
        refused.IsSuccess.ShouldBeFalse("the new cooldown did not run from the second mail");
        refused.Error.ShouldBeOfType<RecipientMailedRecently>().RetryAt.ShouldBe(At(Start.AddMinutes(20)));
    }

    [Fact]
    public void A_refused_mail_changes_nothing()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));

        var refused = recipient.RequestMail(At(Start.AddMinutes(5)), TenMinutes);

        refused.IsSuccess.ShouldBeFalse();
        recipient.LastMailedAt.ShouldBe(At(Start));
        recipient.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_stored_instant_less_than_a_cooldown_ahead_is_trusted()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start.AddMinutes(5)));

        var refused = recipient.RequestMail(At(Start), TenMinutes);

        refused.IsSuccess.ShouldBeFalse("a mail stored 5 minutes ahead did not hold the address's cooldown");
        refused.Error.ShouldBeOfType<RecipientMailedRecently>().RetryAt.ShouldBe(At(Start.AddMinutes(15)));
        recipient.LastMailedAt.ShouldBe(At(Start.AddMinutes(5)));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60 * 24 * 365)]
    public void A_stored_instant_a_cooldown_or_more_ahead_is_not_trusted_and_is_replaced_by_now(int minutesAhead)
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start.AddMinutes(minutesAhead)));

        var mailed = recipient.RequestMail(At(Start), TenMinutes);

        mailed.IsSuccess.ShouldBeTrue($"a mail stored {minutesAhead} minutes ahead locked the address out");
        recipient.LastMailedAt.ShouldBe(At(Start), "a stored instant a cooldown or more ahead was kept");
    }

    // #326: the stored instant is checked before anything is added to it, so one within a cooldown of the end of the
    // calendar is distrusted like any other instant a cooldown or more ahead, instead of overflowing.
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void A_stored_instant_within_a_cooldown_of_the_end_of_the_calendar_is_not_trusted_and_is_replaced_by_now(int minutesBeforeTheEnd)
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(DateTimeOffset.MaxValue.AddMinutes(-minutesBeforeTheEnd)));

        var mailed = Should.NotThrow(() => recipient.RequestMail(At(Start), TenMinutes));

        mailed.IsSuccess.ShouldBeTrue($"a mail stored {minutesBeforeTheEnd} minutes before the end of the calendar locked the address out");
        recipient.LastMailedAt.ShouldBe(At(Start), "a stored instant at the end of the calendar was kept");
    }

    [Fact]
    public void A_cooldown_is_its_own_configured_length()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));
        var oneMinute = RecipientCooldown.From(TimeSpan.FromMinutes(1));

        var refused = recipient.RequestMail(At(Start.AddSeconds(59)), oneMinute);
        var mailed = recipient.RequestMail(At(Start.AddMinutes(1)), oneMinute);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.ShouldBeOfType<RecipientMailedRecently>().RetryAt.ShouldBe(At(Start.AddMinutes(1)));
        mailed.IsSuccess.ShouldBeTrue("a one-minute cooldown held for longer than a minute");
    }

    [Fact]
    public void The_errors_factory_names_the_recipient_and_the_instant()
    {
        var id = InvitationRecipientId.For(Anna);

        var error = InvitationRecipientErrors.MailedRecently(id, At(Start), At(Start.AddMinutes(-4)));

        var recently = error.ShouldBeOfType<RecipientMailedRecently>();
        recently.Code.ShouldBe(MailedRecently);
        recently.Recipient.ShouldBe(id);
        recently.RetryAt.ShouldBe(At(Start));
        recently.RefusedAt.ShouldBe(At(Start.AddMinutes(-4)));
    }

    // #294
    [Fact]
    public void A_refused_mail_carries_the_instant_it_was_refused_at()
    {
        var recipient = InvitationRecipient.FirstMailed(Anna, At(Start));

        var refused = recipient.RequestMail(At(Start.AddMinutes(4)), TenMinutes);

        refused.Error.ShouldBeOfType<RecipientMailedRecently>().RefusedAt.ShouldBe(At(Start.AddMinutes(4)));
    }

    private static Timestamp At(DateTimeOffset instant) => Timestamp.From(instant);
}
