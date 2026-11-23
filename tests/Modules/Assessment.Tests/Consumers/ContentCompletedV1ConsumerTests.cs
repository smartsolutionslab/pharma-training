using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

public class ContentCompletedV1ConsumerTests
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly AnswerOptionId Right = AnswerOptionId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId Wrong = AnswerOptionId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId Question = QuestionVersionId.From(Guid.CreateVersion7());

    private readonly InMemoryAssessmentAggregateStore examinations = new();
    private readonly InMemoryHandledContentCompletions handled = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task The_first_completion_of_a_session_opens_its_examination_unlocked()
    {
        var completed = Completed();

        await HandleAsync(completed);

        var examination = await LoadAsync(completed);
        examination.Status.ShouldBe(ExaminationStatus.Unlocked);
        examination.Session.ShouldBe(LearningSessionId.From(completed.SessionId));
        examination.Learner.ShouldBe(PersonId.From(completed.PersonId));
        examination.Course.ShouldBe(CourseId.From(completed.CourseId));
        examination.CourseVersion.ShouldBe(CourseVersionId.From(completed.CourseVersionId));
        Stream(completed).ShouldHaveSingleItem().ShouldBeOfType<ExaminationOpened>().OpenedAt.ShouldBe(Timestamp.From(Start));
        handled.Handled.ShouldHaveSingleItem().EventIdentifier.ShouldBe(completed.Metadata.EventIdentifier);
    }

    [Fact]
    public async Task The_examination_is_stored_under_the_id_derived_from_the_session()
    {
        var completed = Completed();

        await HandleAsync(completed);

        examinations.Streams.Keys.ShouldHaveSingleItem().ShouldBe(ExaminationId.For(LearningSessionId.From(completed.SessionId)).Value);
        (await LoadAsync(completed)).Id.ShouldBe(ExaminationId.For(LearningSessionId.From(completed.SessionId)));
    }

    [Fact]
    public async Task The_same_completion_delivered_again_changes_nothing()
    {
        var completed = Completed();
        await HandleAsync(completed);

        await HandleAsync(completed);

        Stream(completed).ShouldHaveSingleItem().ShouldBeOfType<ExaminationOpened>();
        handled.Handled.ShouldHaveSingleItem();
        handled.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_completion_delivered_again_after_exhaustion_does_not_unlock()
    {
        var completed = Completed();
        await HandleAsync(completed);
        await ExhaustAsync(completed);

        // A redelivery, or a replay of the dead-lettered event, must not grant three attempts the learner did not earn.
        await HandleAsync(completed);

        Stream(completed)[^1].ShouldBeOfType<ExaminationExhausted>();
        (await LoadAsync(completed)).Status.ShouldBe(ExaminationStatus.Exhausted);
    }

    [Fact]
    public async Task A_new_completion_after_exhaustion_unlocks_three_new_attempts()
    {
        var completed = Completed();
        await HandleAsync(completed);
        await ExhaustAsync(completed);
        time.Advance(TimeSpan.FromDays(2));

        await HandleAsync(Again(completed));

        Stream(completed)[^1].ShouldBeOfType<ExaminationUnlocked>().UnlockedAt.ShouldBe(Timestamp.From(Start.AddDays(2)));
        var examination = await LoadAsync(completed);
        examination.Status.ShouldBe(ExaminationStatus.Unlocked);
        examination.StartAttempt(AttemptId.New(), [Question], Timestamp.From(time.GetUtcNow())).IsSuccess.ShouldBeTrue();
        examination.Attempts[^1].Number.ShouldBe(AttemptNumber.First);
        handled.Handled.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_completion_for_an_unlocked_or_passed_examination_raises_nothing()
    {
        var unlocked = Completed();
        await HandleAsync(unlocked);
        var passed = Completed();
        await HandleAsync(passed);
        await PassAsync(passed);
        var unlockedEvents = Stream(unlocked).Count;
        var passedEvents = Stream(passed).Count;

        await HandleAsync(Again(unlocked));
        await HandleAsync(Again(passed));

        Stream(unlocked).Count.ShouldBe(unlockedEvents);
        Stream(passed).Count.ShouldBe(passedEvents);
        (await LoadAsync(unlocked)).Status.ShouldBe(ExaminationStatus.Unlocked);
        (await LoadAsync(passed)).Status.ShouldBe(ExaminationStatus.Passed);
        handled.Handled.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("another learner")]
    [InlineData("another course")]
    [InlineData("another version")]
    public async Task A_completion_naming_another_learner_or_version_for_a_known_session_is_refused(string mismatch)
    {
        var completed = Completed();
        await HandleAsync(completed);
        await ExhaustAsync(completed);
        var other = Again(completed) switch
        {
            var again when mismatch == "another learner" => again with { PersonId = Guid.CreateVersion7() },
            var again when mismatch == "another course" => again with { CourseId = Guid.CreateVersion7() },
            var again when mismatch == "another version" => again with { CourseVersionId = Guid.CreateVersion7() },
            _ => throw new InvalidOperationException($"{mismatch} is no mismatch this test knows."),
        };

        await Should.ThrowAsync<GuardException>(() => HandleAsync(other));

        Stream(completed)[^1].ShouldBeOfType<ExaminationExhausted>();
        handled.Handled.ShouldHaveSingleItem().EventIdentifier.ShouldBe(completed.Metadata.EventIdentifier);
    }

    [Theory]
    [InlineData(nameof(ContentCompletedV1.SessionId))]
    [InlineData(nameof(ContentCompletedV1.PersonId))]
    [InlineData(nameof(ContentCompletedV1.CourseId))]
    [InlineData(nameof(ContentCompletedV1.CourseVersionId))]
    [InlineData(nameof(EventMetadata.EventIdentifier))]
    public async Task A_completion_with_an_empty_id_is_refused_by_the_field(string field)
    {
        var completed = Completed();
        var malformed = field switch
        {
            nameof(ContentCompletedV1.SessionId) => completed with { SessionId = Guid.Empty },
            nameof(ContentCompletedV1.PersonId) => completed with { PersonId = Guid.Empty },
            nameof(ContentCompletedV1.CourseId) => completed with { CourseId = Guid.Empty },
            nameof(ContentCompletedV1.CourseVersionId) => completed with { CourseVersionId = Guid.Empty },
            nameof(EventMetadata.EventIdentifier) => completed with { Metadata = new EventMetadata(Guid.Empty, Start) },
            _ => throw new InvalidOperationException($"{field} is no id of the event."),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(malformed));

        refused.ParamName.ShouldBe(field);
        examinations.Streams.ShouldBeEmpty();
        handled.Handled.ShouldBeEmpty();
        handled.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_first_completion_with_an_empty_event_identifier_does_not_swallow_a_second_for_another_session()
    {
        var first = Completed() with { Metadata = new EventMetadata(Guid.Empty, Start) };
        var second = Completed() with { Metadata = new EventMetadata(Guid.Empty, Start) };
        await DeliverIgnoringRefusalAsync(first);

        // Had the first been recorded under the empty identifier, the second would be dropped as already handled.
        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(second));

        refused.ParamName.ShouldBe(nameof(EventMetadata.EventIdentifier));
        examinations.Streams.ShouldNotContainKey(ExaminationId.For(LearningSessionId.From(second.SessionId)).Value);
    }

    [Fact]
    public async Task A_commit_that_fails_after_the_unlock_was_saved_records_nothing_and_its_retry_unlocks_once()
    {
        var completed = Completed();
        await HandleAsync(completed);
        await ExhaustAsync(completed);
        var again = Again(completed);
        handled.ThrowOnNextCommit();

        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(again));

        // The stream is saved first: the unlock is stored, but the identifier is not recorded.
        Stream(completed)[^1].ShouldBeOfType<ExaminationUnlocked>();
        handled.Handled.ShouldNotContain(row => row.EventIdentifier == again.Metadata.EventIdentifier);

        // Wolverine retries the message: it finds the unlocked stream, unlocks nothing more and records the identifier.
        await HandleAsync(again);

        Stream(completed).OfType<ExaminationUnlocked>().ShouldHaveSingleItem();
        handled.Handled.Count(row => row.EventIdentifier == again.Metadata.EventIdentifier).ShouldBe(1);
    }

    private static ContentCompletedV1 Completed() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7())
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), Start),
        };

    /// <summary>Learning completed the same session's content once more: the same session, another event.</summary>
    private static ContentCompletedV1 Again(ContentCompletedV1 completed) =>
        completed with { Metadata = new EventMetadata(Guid.CreateVersion7(), Start) };

    private static TestDefinition DefinitionFor(ContentCompletedV1 completed) =>
        new(
            CourseVersionId.From(completed.CourseVersionId),
            [new DefinedQuestion(Question, ChapterId.From(Guid.CreateVersion7()), [Right, Wrong], [Right])],
            ScorePercent.From(100),
            AttemptNumber.From(3));

    private Task HandleAsync(ContentCompletedV1 completed) =>
        new ContentCompletedV1Consumer(examinations, handled, handled, time).HandleAsync(completed, TestContext.Current.CancellationToken);

    private async Task DeliverIgnoringRefusalAsync(ContentCompletedV1 completed)
    {
        try
        {
            await HandleAsync(completed);
        }
        catch (GuardException)
        {
            // Refused or not, what matters is what the next delivery finds.
        }
    }

    private List<IDomainEvent> Stream(ContentCompletedV1 completed) =>
        examinations.Streams[ExaminationId.For(LearningSessionId.From(completed.SessionId)).Value];

    private async Task<Examination> LoadAsync(ContentCompletedV1 completed)
    {
        var loaded = await examinations.LoadAsync<Examination>(ExaminationId.For(LearningSessionId.From(completed.SessionId)), TestContext.Current.CancellationToken);
        loaded.HasValue.ShouldBeTrue("the session's examination is stored");
        return loaded.Value;
    }

    /// <summary>Three failed attempts through the domain, saved: the examination is exhausted.</summary>
    private async Task ExhaustAsync(ContentCompletedV1 completed)
    {
        var examination = await LoadAsync(completed);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Submit(examination, DefinitionFor(completed), Wrong).ShouldBe(AttemptOutcome.Failed);
        }

        examination.Status.ShouldBe(ExaminationStatus.Exhausted);
        await examinations.SaveAsync(examination, TestContext.Current.CancellationToken);
    }

    private async Task PassAsync(ContentCompletedV1 completed)
    {
        var examination = await LoadAsync(completed);
        Submit(examination, DefinitionFor(completed), Right).ShouldBe(AttemptOutcome.Passed);
        await examinations.SaveAsync(examination, TestContext.Current.CancellationToken);
    }

    private AttemptOutcome Submit(Examination examination, TestDefinition definition, AnswerOptionId chosen)
    {
        var now = Timestamp.From(time.GetUtcNow());
        examination.StartAttempt(AttemptId.New(), [Question], now).IsSuccess.ShouldBeTrue();
        examination.Answer(Question, [chosen], now).IsSuccess.ShouldBeTrue();
        return examination.Submit(definition, now).Value;
    }
}
