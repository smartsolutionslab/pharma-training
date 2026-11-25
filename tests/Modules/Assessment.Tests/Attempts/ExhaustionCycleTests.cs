using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The lock and unlock cycle through the use cases: three failed submits announce the lock with AttemptsExhaustedV1,
/// Learning's next ContentCompletedV1 unlocks three new attempts, and their failures lock and announce again.
/// </summary>
public class ExhaustionCycleTests
{
    private readonly AttemptFixture fixture = new();
    private readonly InMemoryHandledContentCompletions handled = new();

    [Fact]
    public async Task A_completion_after_the_announced_lock_unlocks_three_new_attempts_which_lock_and_announce_again()
    {
        await fixture.OpenAsync();
        await FailThreeTimesAsync();
        fixture.UnitOfWork.Published.OfType<AttemptsExhaustedV1>().ShouldHaveSingleItem();
        (await fixture.StartAsync()).Error.Code.ShouldBe("assessment.examination-not-unlocked");
        fixture.Time.Advance(TimeSpan.FromHours(1));

        var completed = Completed();
        await DeliverAsync(completed);
        await DeliverAsync(completed);

        var restarted = await fixture.StartAsync();
        restarted.IsSuccess.ShouldBeTrue();
        (await fixture.CurrentAttemptAsync()).Value.Number.ShouldBe(1);
        (await fixture.AnswerAsync(Q1, D)).IsSuccess.ShouldBeTrue();
        (await fixture.SubmitAsync()).IsSuccess.ShouldBeTrue();
        await FailAsync();
        await FailAsync();
        fixture.UnitOfWork.Published.OfType<TestFailedV1>().Select(failure => failure.AttemptNumber).ShouldBe([1, 2, 3, 1, 2, 3]);
        fixture.UnitOfWork.Published.OfType<AttemptsExhaustedV1>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_completion_while_attempts_are_left_neither_unlocks_nor_announces_anything()
    {
        await fixture.OpenAsync();
        await FailAsync();
        var published = fixture.UnitOfWork.Published.Count;
        fixture.Time.Advance(TimeSpan.FromHours(1));

        await DeliverAsync(Completed());

        fixture.UnitOfWork.Published.Count.ShouldBe(published);
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        (await fixture.CurrentAttemptAsync()).Value.Number.ShouldBe(2);
    }

    private async Task FailThreeTimesAsync()
    {
        await FailAsync();
        await FailAsync();
        await FailAsync();
    }

    private async Task FailAsync() => (await fixture.AttemptAsync([D], [B, C])).IsSuccess.ShouldBeTrue();

    private ContentCompletedV1 Completed() =>
        new(fixture.Session.Value, fixture.Learner.Value, fixture.Course.Value, fixture.Version.Value)
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), fixture.Time.GetUtcNow()),
        };

    private Task DeliverAsync(ContentCompletedV1 completed) =>
        new ContentCompletedV1Consumer(fixture.Store, handled, handled, fixture.Time, NullLogger<ContentCompletedV1Consumer>.Instance).HandleAsync(completed, Token);
}
