using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests;

/// <summary>
/// The invitation mail limits are host configuration, read once at start-up: without it an invitation is renewed at
/// most every 10 minutes and a pharmacy mails at most 50 invitations a UTC day; a configured value wins; a value that
/// is no whole number in range stops the host, naming the key.
/// </summary>
public class OrganisationLimitsTests
{
    private const string CooldownKey = "Organisation:InvitationLimits:RenewCooldownMinutes";
    private const string CapKey = "Organisation:InvitationLimits:MailsPerPharmacyPerDay";

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

    private static InvitationMailLimits LimitsWith(params (string Key, string Value)[] settings)
    {
        Dictionary<string, string?> values = new() { ["ConnectionStrings:database"] = "Host=localhost" };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        new OrganisationModule().AddServices(services, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<InvitationMailLimits>();
    }
}
