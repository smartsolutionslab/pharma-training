using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// What each commit of a module's unit of work sends when one scope commits twice. A commit of several (the republish,
/// ADR 0019) sends at each commit exactly the events published since the last one. Every other commit keeps
/// Wolverine's default, <see cref="MultiFlushMode.OnlyOnce"/>: after the scope's first flush, a later commit sends
/// nothing at once, so the events of a save that lost its race and stayed in the context are never sent by a later
/// commit in the same scope. The outbox is <see cref="RecordingOutbox"/>, a real Wolverine message context.
/// </summary>
public class ModuleUnitOfWorkFlushTests
{
    private readonly RecordingOutbox outbox;
    private readonly FlushUnitOfWork unitOfWork;

    public ModuleUnitOfWorkFlushTests()
    {
        // Wolverine's own flush, which TryCommitAsync calls after its commit, reads the durability mode from the options.
        var runtime = Substitute.For<IWolverineRuntime>();
        runtime.Options.Returns(new WolverineOptions());
        outbox = new RecordingOutbox(runtime, RecordingOutbox.UnopenedContext());
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddSingleton<IDbContextOutbox<FlushDbContext>>(outbox)
            .BuildServiceProvider();
        unitOfWork = new FlushUnitOfWork(services);
    }

    [Fact]
    public async Task Each_commit_of_several_sends_only_the_events_published_since_the_last()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await unitOfWork.PublishAsync(new AnnouncedV1(1));
        await unitOfWork.CommitOneOfSeveralAsync(cancellationToken);
        await unitOfWork.PublishAsync(new AnnouncedV1(2));
        await unitOfWork.CommitOneOfSeveralAsync(cancellationToken);

        outbox.Flushes.Select(Numbers).ShouldBe([[1], [2]]);
    }

    [Fact]
    public async Task A_second_commit_in_the_scope_sends_nothing_at_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await unitOfWork.PublishAsync(new AnnouncedV1(1));
        await unitOfWork.CommitAsync(cancellationToken);
        await unitOfWork.PublishAsync(new AnnouncedV1(2));
        await unitOfWork.CommitAsync(cancellationToken);

        outbox.Flushes.Select(Numbers).ShouldBe([[1], []]);
        outbox.MultiFlushMode.ShouldBe(MultiFlushMode.OnlyOnce);
    }

    /// <summary>
    /// TryCommitAsync saves and commits itself and then calls Wolverine's flush on the context, which this double does not
    /// record, so the test reads the mode that flush obeys.
    /// </summary>
    [Fact]
    public async Task A_commit_that_can_lose_a_race_keeps_wolverines_only_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await unitOfWork.PublishAsync(new AnnouncedV1(1));
        (await unitOfWork.TryCommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        await unitOfWork.PublishAsync(new AnnouncedV1(2));
        (await unitOfWork.TryCommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();

        outbox.MultiFlushMode.ShouldBe(MultiFlushMode.OnlyOnce);
    }

    [Fact]
    public async Task A_commit_of_several_leaves_the_scope_at_only_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await unitOfWork.PublishAsync(new AnnouncedV1(1));
        await unitOfWork.CommitOneOfSeveralAsync(cancellationToken);
        await unitOfWork.PublishAsync(new AnnouncedV1(2));
        await unitOfWork.CommitAsync(cancellationToken);

        outbox.Flushes.Select(Numbers).ShouldBe([[1], []]);
        outbox.MultiFlushMode.ShouldBe(MultiFlushMode.OnlyOnce);
    }

    private static int[] Numbers(IReadOnlyList<object> sent) => [.. sent.Cast<AnnouncedV1>().Select(announced => announced.Number)];
}
