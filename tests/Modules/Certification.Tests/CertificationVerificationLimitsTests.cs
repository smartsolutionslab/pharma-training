using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests;

/// <summary>
/// The limit on public verification (#51, docs/adr/0026) is deployment configuration under
/// <c>Certification:VerificationLimits</c>, 30 a minute and 500 a day per client address unless it says otherwise: the
/// host reads it once at startup, and a value that is not a whole number from 1 stops it there, naming the key.
/// </summary>
public class CertificationVerificationLimitsTests
{
    [Fact]
    public void Without_configuration_verification_allows_30_a_minute_and_500_a_day()
    {
        var limit = CertificationVerificationLimits.From(new ConfigurationBuilder().Build());

        limit.Name.ShouldBe("certification.verify");
        Windows(limit).ShouldBe([(30, TimeSpan.FromMinutes(1)), (500, TimeSpan.FromDays(1))]);
        limit.Refusal.Code.ShouldBe("certification.verification-limit-reached");
        CertificationVerificationLimits.Verify.ShouldBe("certification.verify");
        CertificationVerificationLimits.DefaultPerMinute.ShouldBe(30);
        CertificationVerificationLimits.DefaultPerDay.ShouldBe(500);
    }

    [Fact]
    public void The_keys_are_the_documented_ones()
    {
        CertificationVerificationLimits.PerMinuteKey.ShouldBe("Certification:VerificationLimits:PerMinute");
        CertificationVerificationLimits.PerDayKey.ShouldBe("Certification:VerificationLimits:PerDay");
    }

    [Fact]
    public void A_configured_minute_replaces_its_default()
    {
        var limit = CertificationVerificationLimits.From(Configured(CertificationVerificationLimits.PerMinuteKey, "7"));

        Windows(limit).ShouldBe([(7, TimeSpan.FromMinutes(1)), (500, TimeSpan.FromDays(1))]);
    }

    [Fact]
    public void A_configured_day_replaces_its_default()
    {
        var limit = CertificationVerificationLimits.From(Configured(CertificationVerificationLimits.PerDayKey, "1000"));

        Windows(limit).ShouldBe([(30, TimeSpan.FromMinutes(1)), (1000, TimeSpan.FromDays(1))]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_takes_the_default(string value)
    {
        var limit = CertificationVerificationLimits.From(Configured(CertificationVerificationLimits.PerMinuteKey, value));

        Windows(limit).ShouldBe([(30, TimeSpan.FromMinutes(1)), (500, TimeSpan.FromDays(1))]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("x")]
    [InlineData("5.0")]
    [InlineData("+5")]
    public void An_unreadable_value_stops_the_host_naming_the_key(string value)
    {
        foreach (var key in new[] { CertificationVerificationLimits.PerMinuteKey, CertificationVerificationLimits.PerDayKey })
        {
            var read = () => CertificationVerificationLimits.From(Configured(key, value));

            var refusal = Should.Throw<InvalidOperationException>(read, $"{key} = {value}");
            refusal.Message.ShouldContain(key);
            refusal.Message.ShouldContain("a whole number from 1");
        }
    }

    [Fact]
    public void The_module_registers_the_limit_from_its_configuration()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:database"] = "Host=localhost",
                [CertificationVerificationLimits.PerMinuteKey] = "3",
            })
            .Build();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));

        new CertificationModule().AddServices(services, configuration);

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetRequiredService<UserRateLimits>().Find(CertificationVerificationLimits.Verify);
        registered.HasValue.ShouldBeTrue();
        Windows(registered.Value).ShouldBe([(3, TimeSpan.FromMinutes(1)), (500, TimeSpan.FromDays(1))]);
    }

    private static List<(int Permits, TimeSpan Length)> Windows(UserRateLimit limit) =>
        [.. limit.Windows.Select(window => (window.Permits, window.Length))];

    private static IConfiguration Configured(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
