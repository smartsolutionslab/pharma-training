using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationRecipients;

/// <summary>How long after its latest invitation mail an address may be mailed again: more than nothing, at most a day.</summary>
public class RecipientCooldownTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(1440)]
    public void A_recipient_cooldown_of_a_minute_up_to_a_day_is_a_cooldown(int minutes) =>
        RecipientCooldown.From(TimeSpan.FromMinutes(minutes)).Value.ShouldBe(TimeSpan.FromMinutes(minutes));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void A_recipient_cooldown_of_nothing_less_or_more_than_a_day_is_refused(int minutes) =>
        Should.Throw<GuardException>(() => RecipientCooldown.From(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void A_recipient_cooldown_one_tick_beyond_a_day_is_refused() =>
        Should.Throw<GuardException>(() => RecipientCooldown.From(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)));
}
