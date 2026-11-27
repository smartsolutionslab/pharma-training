using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A module adds its sweep of erased persons' messages (#439) with <c>AddErasedPersonMessageSweep</c>: a hosted service
/// that runs every interval of the module's own <c>ErasedPersonMessageSweep</c> section over the purge the host
/// registers. The interval, the schema and the types are checked when the sweep is added, so a bad one stops the host at
/// startup, never at the first pass.
/// </summary>
public class ErasedPersonMessageSweepsTests
{
    private static readonly IReadOnlyCollection<Type> Types = [typeof(AnnouncedV1)];
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PassTimeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sweep_runs_as_a_hosted_service_with_the_modules_interval()
    {
        var interval = TimeSpan.FromSeconds(7);
        var time = new TimerSignallingTimeProvider(Start);
        var purge = Substitute.For<IPersonalMessagePurge>();
        var passed = new TaskCompletionSource<(string Schema, IReadOnlyCollection<Type> Types)>(TaskCreationOptions.RunContinuationsAsynchronously);
        purge.PurgeErasedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Type>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                passed.TrySetResult((call.ArgAt<string>(0), call.ArgAt<IReadOnlyCollection<Type>>(1)));
                return Task.FromResult(Option<int>.Some(0));
            });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(purge);
        services.AddErasedPersonMessageSweep(Settings(("Learning:ErasedPersonMessageSweep:Interval", "00:00:07")), "Learning", "learning", Types);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var sweep = provider.GetServices<IHostedService>().OfType<ErasedPersonMessageSweep>().ShouldHaveSingleItem();

        await sweep.StartAsync(Token);
        try
        {
            await time.TimerCreated.WaitAsync(PassTimeout, Token);
            time.Advance(interval - TimeSpan.FromMilliseconds(1));
            purge.ReceivedCalls().ShouldBeEmpty("the sweep ran before the module's interval");
            time.Advance(TimeSpan.FromMilliseconds(1));
            var (schema, types) = await passed.Task.WaitAsync(PassTimeout, Token);

            schema.ShouldBe("learning");
            types.ShouldBeSameAs(Types);
        }
        finally
        {
            await sweep.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void A_bad_interval_stops_the_host_when_the_sweep_is_added()
    {
        var services = new ServiceCollection();

        var refused = Should.Throw<InvalidOperationException>(() =>
            services.AddErasedPersonMessageSweep(Settings(("Compliance:ErasedPersonMessageSweep:Interval", "soon")), "Compliance", "compliance", Types));

        refused.Message.ShouldContain("Compliance:ErasedPersonMessageSweep:Interval", Case.Sensitive);
        refused.Message.ShouldContain("'soon'", Case.Sensitive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Learning")]
    [InlineData("learning; drop table x")]
    [InlineData("\"learning\"")]
    public void A_schema_that_is_no_plain_lowercase_identifier_is_refused_when_the_sweep_is_added(string schema)
    {
        var services = new ServiceCollection();

        Should.Throw<GuardException>(() => services.AddErasedPersonMessageSweep(Settings(), "Learning", schema, Types));
    }

    [Fact]
    public void A_sweep_without_message_types_is_refused()
    {
        var services = new ServiceCollection();

        Should.Throw<GuardException>(() => services.AddErasedPersonMessageSweep(Settings(), "Learning", "learning", []));
    }

    private static IConfiguration Settings(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
