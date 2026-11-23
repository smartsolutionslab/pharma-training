using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Runtime.Handlers;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// A consumer that needs longer than Wolverine's default execution timeout (60 seconds) says so with
/// <see cref="ConsumerTimeoutAttribute"/>, on its handler method or its class, and <see cref="ConsumerTimeoutPolicy"/> gives
/// its chain that timeout; every other chain keeps the default.
/// </summary>
public class ConsumerTimeoutPolicyTests
{
    [Fact]
    public void A_handler_method_with_a_consumer_timeout_gets_it()
    {
        Applied<TimedMethodConsumer>().ExecutionTimeoutInSeconds.ShouldBe(30 * 60);
    }

    [Fact]
    public void A_consumer_class_with_a_consumer_timeout_gives_it_to_its_handler()
    {
        Applied<TimedClassConsumer>().ExecutionTimeoutInSeconds.ShouldBe(5 * 60);
    }

    [Fact]
    public void The_handler_methods_timeout_wins_over_its_classs()
    {
        Applied<TimedClassAndMethodConsumer>().ExecutionTimeoutInSeconds.ShouldBe(20 * 60);
    }

    [Fact]
    public void A_consumer_without_one_keeps_wolverines_default_of_sixty_seconds()
    {
        Applied<UntimedConsumer>().ExecutionTimeoutInSeconds.ShouldBeNull();
        new WolverineOptions().DefaultExecutionTimeout.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_timeout_below_one_minute_is_refused(int minutes) =>
        Should.Throw<GuardException>(() => new ConsumerTimeoutAttribute(minutes));

    [Fact]
    public void Use_consumer_timeouts_registers_the_policy_once()
    {
        var options = new WolverineOptions();

        options.UseConsumerTimeouts().UseConsumerTimeouts();

        options.Policies.OfType<ConsumerTimeoutPolicy>().Count().ShouldBe(1);
    }

    private static HandlerChain Applied<TConsumer>()
    {
        var chain = HandlerChain.For<TConsumer>("Handle", new HandlerGraph());
        new ConsumerTimeoutPolicy().Apply([chain], new GenerationRules(), Substitute.For<IServiceContainer>());
        return chain;
    }

    public sealed record Probe(int Value);

    public sealed class TimedMethodConsumer
    {
        public int Handled { get; private set; }

        [ConsumerTimeout(30)]
        public void Handle(Probe message) => Handled += message.Value;
    }

    [ConsumerTimeout(5)]
    public sealed class TimedClassConsumer
    {
        public int Handled { get; private set; }

        public void Handle(Probe message) => Handled += message.Value;
    }

    [ConsumerTimeout(5)]
    public sealed class TimedClassAndMethodConsumer
    {
        public int Handled { get; private set; }

        [ConsumerTimeout(20)]
        public void Handle(Probe message) => Handled += message.Value;
    }

    public sealed class UntimedConsumer
    {
        public int Handled { get; private set; }

        public void Handle(Probe message) => Handled += message.Value;
    }
}
