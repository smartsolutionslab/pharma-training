using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests;

/// <summary>
/// The invitation mail limits are host configuration, read once at start-up: without it an invitation is renewed at
/// most every 10 minutes and a pharmacy mails at most 50 invitations a UTC day; a configured value wins; a value that
/// is no whole number in range stops the host, naming the key. Accepting an invitation is limited per user, 10 a minute
/// and 100 a day without configuration (docs/adr/0004, docs/adr/0020), read the same way.
/// </summary>
public class OrganisationLimitsTests
{
    private const string CooldownKey = "Organisation:InvitationLimits:RenewCooldownMinutes";
    private const string CapKey = "Organisation:InvitationLimits:MailsPerPharmacyPerDay";
    private const string RecipientCooldownKey = "Organisation:InvitationLimits:RecipientCooldownMinutes";
    private const string SenderCapKey = "Organisation:InvitationLimits:MailsPerSenderPerDay";
    private const string AcceptPerMinuteKey = "Organisation:AcceptLimit:PerMinute";
    private const string AcceptPerDayKey = "Organisation:AcceptLimit:PerDay";
    private const string AcceptLimit = "organisation.accept-invitation";

    [Fact]
    public void Without_configuration_the_limits_are_10_minutes_and_50_mails_a_day()
    {
        var limits = LimitsWith();

        limits.RenewCooldown.Value.ShouldBe(TimeSpan.FromMinutes(10));
        limits.DailyMailCap.Value.ShouldBe(50);
        limits.ShouldBe(InvitationMailLimits.Default);
    }

    [Fact]
    public void Configured_values_win()
    {
        var limits = LimitsWith((CooldownKey, "30"), (CapKey, "200"));

        limits.RenewCooldown.Value.ShouldBe(TimeSpan.FromMinutes(30));
        limits.DailyMailCap.Value.ShouldBe(200);
    }

    [Theory]
    [InlineData(CooldownKey, "0")]
    [InlineData(CooldownKey, "-1")]
    [InlineData(CooldownKey, "abc")]
    [InlineData(CooldownKey, "1441")]
    [InlineData(CooldownKey, "2.5")]
    [InlineData(CapKey, "0")]
    [InlineData(CapKey, "abc")]
    [InlineData(CapKey, "10001")]
    public void A_value_that_is_no_whole_number_in_range_stops_the_host_naming_the_key(string key, string value)
    {
        var stopped = Should.Throw<Exception>(() => LimitsWith((key, value)), $"{key} = \"{value}\" started the host");

        stopped.Message.ShouldContain(key);
    }

    [Fact]
    public void Without_configuration_accepting_is_limited_to_10_a_minute_and_100_a_day()
    {
        var limit = AcceptLimitWith();

        limit.Windows.Select(window => (window.Permits, window.Length)).ShouldBe([(10, TimeSpan.FromMinutes(1)), (100, TimeSpan.FromDays(1))]);
        limit.Refusal.Code.ShouldBe("organisation.accept-limit-reached");
        limit.Refusal.Status.ShouldBe(System.Net.HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Configured_accept_limits_win()
    {
        var limit = AcceptLimitWith((AcceptPerMinuteKey, "30"), (AcceptPerDayKey, "500"));

        limit.Windows.Select(window => (window.Permits, window.Length)).ShouldBe([(30, TimeSpan.FromMinutes(1)), (500, TimeSpan.FromDays(1))]);
    }

    [Theory]
    [InlineData(AcceptPerMinuteKey, "0")]
    [InlineData(AcceptPerMinuteKey, "-1")]
    [InlineData(AcceptPerMinuteKey, "ten")]
    [InlineData(AcceptPerMinuteKey, "2.5")]
    [InlineData(AcceptPerDayKey, "0")]
    [InlineData(AcceptPerDayKey, "abc")]
    public void A_bad_accept_limit_stops_the_host_naming_the_key(string key, string value)
    {
        var stopped = Should.Throw<Exception>(() => AcceptLimitWith((key, value)), $"{key} = \"{value}\" started the host");

        stopped.Message.ShouldContain(key);
    }

    // #265: an address is mailed at most once per cooldown and a sender mails at most so many a UTC day.
    [Fact]
    public void Without_configuration_an_address_is_mailed_at_most_every_10_minutes_and_a_sender_sends_at_most_100_a_day()
    {
        var limits = RecipientAndSenderLimitsWith();

        limits.RecipientCooldown.Value.ShouldBe(TimeSpan.FromMinutes(10));
        limits.MailsPerSenderPerDay.Value.ShouldBe(100);
        limits.ShouldBe(RecipientAndSenderMailLimits.Default);
    }

    [Fact]
    public void Configured_recipient_and_sender_limits_win()
    {
        var limits = RecipientAndSenderLimitsWith((RecipientCooldownKey, "30"), (SenderCapKey, "1000"));

        limits.RecipientCooldown.Value.ShouldBe(TimeSpan.FromMinutes(30));
        limits.MailsPerSenderPerDay.Value.ShouldBe(1000);
    }

    [Theory]
    [InlineData(RecipientCooldownKey, "0")]
    [InlineData(RecipientCooldownKey, "-1")]
    [InlineData(RecipientCooldownKey, "abc")]
    [InlineData(RecipientCooldownKey, "1441")]
    [InlineData(RecipientCooldownKey, "2.5")]
    [InlineData(SenderCapKey, "0")]
    [InlineData(SenderCapKey, "abc")]
    [InlineData(SenderCapKey, "10001")]
    public void A_recipient_or_sender_limit_that_is_no_whole_number_in_range_stops_the_host_naming_the_key(string key, string value)
    {
        var stopped = Should.Throw<Exception>(() => RecipientAndSenderLimitsWith((key, value)), $"{key} = \"{value}\" started the host");

        stopped.Message.ShouldContain(key);
    }

    [Fact]
    public void The_keys_are_the_ones_the_host_is_configured_with()
    {
        OrganisationLimits.RecipientCooldownMinutesKey.ShouldBe(RecipientCooldownKey);
        OrganisationLimits.MailsPerSenderPerDayKey.ShouldBe(SenderCapKey);
    }

    private static RecipientAndSenderMailLimits RecipientAndSenderLimitsWith(params (string Key, string Value)[] settings)
    {
        using var provider = ServicesWith(settings);
        return provider.GetRequiredService<RecipientAndSenderMailLimits>();
    }

    private static UserRateLimit AcceptLimitWith(params (string Key, string Value)[] settings)
    {
        using var provider = ServicesWith(settings);
        var limits = provider.GetService<UserRateLimits>();
        limits.ShouldNotBeNull("the module registered no per-user limit");
        var limit = limits.Find(AcceptLimit);
        limit.HasValue.ShouldBeTrue($"the module registered no limit named {AcceptLimit}");
        return limit.Value;
    }

    private static InvitationMailLimits LimitsWith(params (string Key, string Value)[] settings)
    {
        using var provider = ServicesWith(settings);
        return provider.GetRequiredService<InvitationMailLimits>();
    }

    private static ServiceProvider ServicesWith(params (string Key, string Value)[] settings)
    {
        Dictionary<string, string?> values = new() { ["ConnectionStrings:database"] = "Host=localhost" };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        new OrganisationModule().AddServices(services, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }
}
