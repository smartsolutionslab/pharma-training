using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// One pass of Assessment's announcement sweep (#277): every stored ExaminationPassed that has no marker, because the
/// commit of its announcement never happened, is announced again as the submit would have announced it, oldest first,
/// each with its marker in a commit of its own. The pass keeps the time the test was passed, never its own. A pass asks
/// only for the events stored between the lookback and the grace period ago, at most a batch of them.
/// </summary>
public class ReannounceTestPassesTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly List<string> steps = [];
    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryUnannouncedTestPasses unannounced = new();
    private readonly FakeLogger<ReannounceTestPassesHandler> logger = new();
    private readonly RecordingAssessmentUnitOfWork unitOfWork;
    private readonly InMemoryAnnouncedEvents announced;

    public ReannounceTestPassesTests()
    {
        unitOfWork = new RecordingAssessmentUnitOfWork(steps);
        announced = new InMemoryAnnouncedEvents(steps);
    }

    [Fact]
    public async Task A_pass_announces_each_unannounced_test_pass_with_its_marker_one_commit_each()
    {
        var older = Stored(streamVersion: 6, storedAt: Now.AddHours(-5));
        var newer = Stored(streamVersion: 11, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([older, newer]);

        var passed = await PassAsync(Settings());

        passed.IsSuccess.ShouldBeTrue();

        // The recording unit of work leaves the metadata to the real one, so the payloads compare as the mapping builds them.
        unitOfWork.Published.ShouldBe([older.Passed.ToIntegrationEvent(), newer.Passed.ToIntegrationEvent()]);
        announced.Added.Select(marker => (marker.StreamId, marker.StreamVersion, marker.AnnouncedAt))
            .ShouldBe([(older.Passed.Examination.Value, 6L, Now), (newer.Passed.Examination.Value, 11L, Now)]);
        unitOfWork.Commits.ShouldBe(2);
        steps.ShouldBe(
        [
            RecordingAssessmentUnitOfWork.Queued, InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed,
            RecordingAssessmentUnitOfWork.Queued, InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed,
        ]);
    }

    [Fact]
    public async Task A_test_pass_announced_again_keeps_the_time_it_was_passed()
    {
        var passedAt = Now.AddHours(-26);
        unannounced.Stored.Add(Stored(streamVersion: 6, storedAt: passedAt));
        time.Advance(TimeSpan.FromMinutes(3));

        await PassAsync(Settings());

        var announcedPass = unitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestPassedV1>();
        announcedPass.PassedAt.ShouldBe(passedAt);
        announcedPass.PassedAt.ShouldNotBe(time.GetUtcNow());
    }

    [Fact]
    public async Task A_pass_asks_for_the_window_between_lookback_and_grace_period_and_the_batch_size()
    {
        time.Advance(TimeSpan.FromMinutes(42));
        var now = time.GetUtcNow();

        await PassAsync(Settings(gracePeriod: "00:00:30", lookback: "2.00:00:00", batchSize: "7"));

        var asked = unannounced.Asked.ShouldHaveSingleItem();
        asked.StoredFrom.ShouldBe(now - TimeSpan.FromDays(2));
        asked.StoredUntil.ShouldBe(now - TimeSpan.FromSeconds(30));
        asked.Limit.ShouldBe(7);
    }

    [Fact]
    public async Task A_pass_with_nothing_unannounced_commits_nothing()
    {
        var passed = await PassAsync(Settings());

        passed.IsSuccess.ShouldBeTrue();
        unannounced.Asked.ShouldHaveSingleItem();
        unitOfWork.Published.ShouldBeEmpty();
        announced.Added.ShouldBeEmpty();
        unitOfWork.Commits.ShouldBe(0);
        steps.ShouldBeEmpty();
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_pass_logs_a_warning_per_test_pass_it_announces_again_naming_its_stream_and_version()
    {
        var lost = Stored(streamVersion: 12, storedAt: Now.AddMinutes(-3));
        unannounced.Stored.Add(lost);

        await PassAsync(Settings());

        var warning = logger.Collector.GetSnapshot().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(lost.Passed.Examination.Value.ToString(), Case.Insensitive);
        warning.Message.ShouldContain("12", Case.Sensitive);
        warning.Message.ShouldNotContain(lost.Passed.Learner.Value.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task A_pass_keys_the_marker_on_the_stream_the_test_pass_is_stored_in()
    {
        var stream = Guid.CreateVersion7();
        var lost = Stored(streamVersion: 3, storedAt: Now.AddMinutes(-5)) with { Stream = stream };
        unannounced.Stored.Add(lost);

        await PassAsync(Settings());

        var marker = announced.Added.ShouldHaveSingleItem();
        marker.StreamId.ShouldBe(stream);
        marker.StreamVersion.ShouldBe(3);
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Warning && record.Message.Contains(stream.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_test_pass_that_cannot_be_read_is_skipped_with_a_warning_and_the_others_are_announced()
    {
        var unreadable = Stored(streamVersion: 2, storedAt: Now.AddHours(-6));
        var older = Stored(streamVersion: 6, storedAt: Now.AddHours(-5));
        var newer = Stored(streamVersion: 11, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([unreadable, older, newer]);
        unannounced.Unreadable.Add(unreadable);

        var passed = await PassAsync(Settings());

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBe([older.Passed.ToIntegrationEvent(), newer.Passed.ToIntegrationEvent()]);
        announced.Added.Select(marker => (marker.StreamId, marker.StreamVersion))
            .ShouldBe([(older.Passed.Examination.Value, 6L), (newer.Passed.Examination.Value, 11L)]);
        unitOfWork.Commits.ShouldBe(2);
        var skipped = logger.Collector.GetSnapshot()
            .Where(record => record.Level == LogLevel.Warning && record.Message.Contains(unreadable.Passed.Examination.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();
        skipped.Message.ShouldContain("2", Case.Sensitive);
        skipped.Message.ShouldNotContain(unreadable.Passed.Learner.Value.ToString(), Case.Insensitive);
        skipped.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_batch_of_test_passes_that_cannot_be_read_holds_up_no_newer_test_pass()
    {
        // Unreadable events at the old end of the window fill the first page; the pass reads on after the last of them.
        List<StoredTestPass> unreadable = [.. Enumerable.Range(1, 3).Select(version => Stored(streamVersion: version, storedAt: Now.AddDays(-6)))];
        var readable = Stored(streamVersion: 7, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([.. unreadable, readable]);
        unannounced.Unreadable.UnionWith(unreadable);

        var passed = await PassAsync(Settings(batchSize: "3"));

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBe([readable.Passed.ToIntegrationEvent()]);
        announced.Added.ShouldHaveSingleItem().StreamVersion.ShouldBe(7);
        unitOfWork.Commits.ShouldBe(1);
        unannounced.Asked.Select(question => question.After).ShouldBe([UnannouncedEvent.BeforeFirst, 3L]);
    }

    [Fact]
    public async Task A_pass_that_reads_past_skipped_test_passes_still_announces_at_most_a_batch()
    {
        var unreadable = Stored(streamVersion: 1, storedAt: Now.AddHours(-6));
        var first = Stored(streamVersion: 2, storedAt: Now.AddHours(-5));
        var second = Stored(streamVersion: 3, storedAt: Now.AddHours(-4));
        var third = Stored(streamVersion: 4, storedAt: Now.AddHours(-3));
        unannounced.Stored.AddRange([unreadable, first, second, third]);
        unannounced.Unreadable.Add(unreadable);

        await PassAsync(Settings(batchSize: "2"));

        unitOfWork.Published.ShouldBe([first.Passed.ToIntegrationEvent(), second.Passed.ToIntegrationEvent()]);
        unitOfWork.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_pass_reads_at_most_its_most_pages_of_test_passes()
    {
        List<StoredTestPass> unreadable = [.. Enumerable.Range(1, AnnouncementSweepSettings.MostPagesPerPass + 1).Select(version => Stored(streamVersion: version, storedAt: Now.AddDays(-6)))];
        var readable = Stored(streamVersion: 99, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([.. unreadable, readable]);
        unannounced.Unreadable.UnionWith(unreadable);

        var passed = await PassAsync(Settings(batchSize: "1"));

        passed.IsSuccess.ShouldBeTrue();
        unannounced.Asked.Count.ShouldBe(AnnouncementSweepSettings.MostPagesPerPass);
        unitOfWork.Published.ShouldBeEmpty();
        unitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_pass_at_the_largest_batch_reads_at_most_its_most_events_of_unreadable_test_passes()
    {
        // #351: ten pages of the largest batch would load and warn of ten thousand events a pass on every replica.
        List<StoredTestPass> unreadable = [.. Enumerable.Range(1, AnnouncementSweepSettings.MostEventsPerPass + 1).Select(version => Stored(streamVersion: version, storedAt: Now.AddDays(-6)))];
        unannounced.Stored.AddRange(unreadable);
        unannounced.Unreadable.UnionWith(unreadable);

        var passed = await PassAsync(Settings(batchSize: AnnouncementSweepSettings.MostBatchSize.ToString(CultureInfo.InvariantCulture)));

        passed.IsSuccess.ShouldBeTrue();
        unannounced.Asked.Sum(question => question.Limit).ShouldBe(AnnouncementSweepSettings.MostEventsPerPass);
        logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Warning).ShouldBe(AnnouncementSweepSettings.MostEventsPerPass);
        unitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_database_that_fails_while_an_event_is_loaded_ends_the_pass_with_the_failure()
    {
        // An outage is not unreadable data: it is not skipped with a warning but ends the pass, which the periodic
        // service logs as an error; nothing after it is announced.
        var first = Stored(streamVersion: 2, storedAt: Now.AddHours(-6));
        var second = Stored(streamVersion: 3, storedAt: Now.AddHours(-5));
        unannounced.Stored.AddRange([first, second]);
        unannounced.Unreachable.Add(first);

        await Should.ThrowAsync<DatabaseUnavailable>(() => PassAsync(Settings()));

        unitOfWork.Published.ShouldBeEmpty();
        unitOfWork.Commits.ShouldBe(0);
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Warning);
    }

    private static StoredTestPass Stored(long streamVersion, DateTimeOffset storedAt)
    {
        var session = LearningSessionId.From(Guid.CreateVersion7());
        var passed = new ExaminationPassed(
            ExaminationId.For(session),
            session,
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            AttemptId.New(),
            ScorePercent.From(90),
            Timestamp.From(storedAt));
        return new StoredTestPass(passed, streamVersion, storedAt);
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

    private Task<Result<Unit, ApiError>> PassAsync(AnnouncementSweepSettings settings) =>
        new ReannounceTestPassesHandler(unannounced, announced, unitOfWork, settings, time, logger)
            .HandleAsync(new ReannounceTestPasses(), TestContext.Current.CancellationToken);
}
