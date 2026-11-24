using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestFailures;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// One pass of Assessment's sweep of failures (#307): every stored failed AttemptSubmitted that has no marker, because the
/// commit of its announcement never happened, is announced again as the submit would have announced it (TestFailedV1,
/// with the attempt's number from its examination), oldest first, each with its marker in a commit of its own. A passed
/// AttemptSubmitted is announced by its ExaminationPassed; one found without a marker is marked and announced by nothing.
/// The pass reads the window and pages as the sweep of passes does (#277, #337, #351, #360).
/// </summary>
public class ReannounceTestFailuresTests
{
    private readonly AttemptFixture examination = new();
    private readonly List<string> steps = [];
    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryUnannouncedExaminationEvents<AttemptSubmitted> unannounced = new();
    private readonly FakeLogger<ReannounceTestFailuresHandler> logger = new();
    private readonly RecordingAssessmentUnitOfWork unitOfWork;
    private readonly InMemoryAnnouncedEvents announced;

    public ReannounceTestFailuresTests()
    {
        unitOfWork = new RecordingAssessmentUnitOfWork(steps);
        announced = new InMemoryAnnouncedEvents(steps);
    }

    [Fact]
    public async Task A_pass_announces_each_unannounced_failure_as_the_submit_did_with_its_marker_one_commit_each()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        await examination.AttemptThroughTheDomainAsync([A], [E]);
        var (first, second) = (await FailuresAsync()) switch { [var one, var two] => (one, two), var other => throw new InvalidOperationException($"{other.Count} failures") };
        unannounced.Stored.AddRange([first, second]);

        var passed = await PassAsync();

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.Cast<TestFailedV1>().Select(failure => (failure.AttemptId, failure.AttemptNumber, failure.SessionId, failure.PersonId, failure.CourseId, failure.CourseVersionId))
            .ShouldBe([
                (first.Event.Attempt.Value, 1, examination.Session.Value, examination.Learner.Value, examination.Course.Value, examination.Version.Value),
                (second.Event.Attempt.Value, 2, examination.Session.Value, examination.Learner.Value, examination.Course.Value, examination.Version.Value),
            ]);
        unitOfWork.Published.ShouldBe([first.Event.ToIntegrationEvent(first.Examination), second.Event.ToIntegrationEvent(second.Examination)]);
        announced.Added.Select(marker => (marker.StreamId, marker.StreamVersion, marker.AnnouncedAt))
            .ShouldBe([(examination.Examination.Value, first.StreamVersion, Now), (examination.Examination.Value, second.StreamVersion, Now)]);
        steps.ShouldBe(
        [
            RecordingAssessmentUnitOfWork.Queued, InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed,
            RecordingAssessmentUnitOfWork.Queued, InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed,
        ]);
    }

    [Fact]
    public async Task A_passed_attempt_found_without_a_marker_is_marked_and_announced_by_nothing()
    {
        // Its announcement is the pass's TestPassedV1, which the sweep of passes looks after; marked, it is never read again.
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([A], [B, C]);
        var stream = examination.Stream();
        var index = stream.ToList().FindIndex(stored => stored is AttemptSubmitted);
        var submitted = (AttemptSubmitted)stream[index];
        submitted.Outcome.ShouldBe(AttemptOutcome.Passed);
        unannounced.Stored.Add(new PlantedExaminationEvent<AttemptSubmitted>(submitted, await LoadedAsync(), index + 1L, Now.AddMinutes(-10)));

        var passed = await PassAsync();

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBeEmpty();
        announced.Added.ShouldHaveSingleItem().StreamVersion.ShouldBe(index + 1L);
        steps.ShouldBe([InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed]);
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_pass_logs_a_warning_per_failure_it_announces_again_naming_its_stream_and_version_never_the_learner()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        var lost = (await FailuresAsync()).ShouldHaveSingleItem();
        unannounced.Stored.Add(lost);

        await PassAsync();

        var warning = logger.Collector.GetSnapshot().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(nameof(AttemptSubmitted), Case.Sensitive);
        warning.Message.ShouldContain(examination.Examination.Value.ToString(), Case.Insensitive);
        warning.GetStructuredStateValue("StreamVersion").ShouldBe(lost.StreamVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        warning.Message.ShouldNotContain(examination.Learner.Value.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task A_pass_asks_for_the_window_between_lookback_and_grace_period_and_the_batch_size()
    {
        time.Advance(TimeSpan.FromMinutes(42));
        var now = time.GetUtcNow();

        await PassAsync(Settings(gracePeriod: "00:00:30", lookback: "2.00:00:00", batchSize: "7"));

        var asked = unannounced.Asked.ShouldHaveSingleItem();
        (asked.StoredFrom, asked.StoredUntil, asked.After, asked.Limit).ShouldBe((now - TimeSpan.FromDays(2), now - TimeSpan.FromSeconds(30), UnannouncedEvent.BeforeFirst, 7));
        unitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_failure_that_cannot_be_read_is_skipped_with_one_warning_and_the_others_are_announced()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        await examination.AttemptThroughTheDomainAsync([A], [E]);
        var (unreadable, readable) = (await FailuresAsync()) switch { [var one, var two] => (one, two), var other => throw new InvalidOperationException($"{other.Count} failures") };
        unannounced.Stored.AddRange([unreadable, readable]);
        unannounced.Unreadable.Add(unreadable);

        var passed = await PassAsync();

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBe([readable.Event.ToIntegrationEvent(readable.Examination)]);
        announced.Added.ShouldHaveSingleItem().StreamVersion.ShouldBe(readable.StreamVersion);
        var skipped = logger.Collector.GetSnapshot().Where(record => record.GetStructuredStateValue("Count") != null).ShouldHaveSingleItem();
        skipped.Level.ShouldBe(LogLevel.Warning);
        skipped.GetStructuredStateValue("Count").ShouldBe("1");
        skipped.GetStructuredStateValue("FirstStreamVersion").ShouldBe(unreadable.StreamVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        skipped.GetStructuredStateValue("FirstFailureType").ShouldBe(nameof(JsonException));
        skipped.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_database_that_fails_while_a_failure_is_loaded_ends_the_pass_with_the_failure()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        var lost = (await FailuresAsync()).ShouldHaveSingleItem();
        unannounced.Stored.Add(lost);
        unannounced.Unreachable.Add(lost);

        await Should.ThrowAsync<DatabaseUnavailable>(() => PassAsync());

        unitOfWork.Commits.ShouldBe(0);
        announced.Added.ShouldBeEmpty();
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_pass_announces_at_most_a_batch_of_failures()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        await examination.AttemptThroughTheDomainAsync([A], [E]);
        unannounced.Stored.AddRange(await FailuresAsync());

        await PassAsync(Settings(batchSize: "1"));

        unitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestFailedV1>().AttemptNumber.ShouldBe(1);
        unitOfWork.Commits.ShouldBe(1);
    }

    private static AnnouncementSweepSettings Settings(string? gracePeriod = null, string? lookback = null, string? batchSize = null) =>
        AnnouncementSweepSettings.From(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Assessment:AnnouncementSweep:GracePeriod", gracePeriod),
                    new KeyValuePair<string, string?>("Assessment:AnnouncementSweep:Lookback", lookback),
                    new KeyValuePair<string, string?>("Assessment:AnnouncementSweep:BatchSize", batchSize),
                ])
                .Build(),
            "Assessment");

    /// <summary>The examination as its stream holds it now.</summary>
    private async Task<Examination> LoadedAsync() => (await examination.Store.LoadAsync<Examination>(examination.Examination, Token)).Value;

    /// <summary>Every failed AttemptSubmitted of the examination's stream, at its version, stored a while ago, with the examination.</summary>
    private async Task<List<PlantedExaminationEvent<AttemptSubmitted>>> FailuresAsync()
    {
        var loaded = await LoadedAsync();
        return [.. examination.Stream()
            .Select((stored, index) => (Stored: stored, StreamVersion: index + 1L))
            .Where(stored => stored.Stored is AttemptSubmitted { Outcome: AttemptOutcome.Failed })
            .Select(stored => new PlantedExaminationEvent<AttemptSubmitted>((AttemptSubmitted)stored.Stored, loaded, stored.StreamVersion, Now.AddMinutes(-10)))];
    }

    private Task<Result<Unit, ApiError>> PassAsync(AnnouncementSweepSettings? settings = null) =>
        new ReannounceTestFailuresHandler(unannounced, announced, unitOfWork, settings ?? Settings(), time, logger)
            .HandleAsync(new ReannounceTestFailures(), TestContext.Current.CancellationToken);
}
