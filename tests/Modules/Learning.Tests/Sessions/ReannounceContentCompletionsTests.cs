using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions.InMemoryLearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// One pass of Learning's announcement sweep (#277): every stored ContentCompleted that has no marker, because the
/// commit of its announcement never happened, is announced again as the report would have announced it, oldest first,
/// each with its marker in a commit of its own. A pass asks only for the events stored between the lookback and the
/// grace period ago, at most a batch of them.
/// </summary>
public class ReannounceContentCompletionsTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryLearningSessions unitOfWork = new();
    private readonly InMemoryUnannouncedContentCompletions unannounced = new();
    private readonly FakeLogger<ReannounceContentCompletionsHandler> logger = new();
    private readonly InMemoryAnnouncedEvents announced;

    public ReannounceContentCompletionsTests()
    {
        announced = new InMemoryAnnouncedEvents(unitOfWork.Steps);
    }

    [Fact]
    public async Task A_pass_announces_each_unannounced_completion_with_its_marker_one_commit_each()
    {
        var older = Stored(streamVersion: 9, storedAt: Now.AddHours(-5));
        var newer = Stored(streamVersion: 4, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([older, newer]);

        var passed = await PassAsync(Settings());

        passed.IsSuccess.ShouldBeTrue();
        // The in-memory unit of work leaves the metadata to the real one, so the payloads compare as the mapping builds them.
        unitOfWork.Published.ShouldBe([older.Completed.ToIntegrationEvent(), newer.Completed.ToIntegrationEvent()]);
        announced.Added.Select(marker => (marker.StreamId, marker.StreamVersion, marker.AnnouncedAt))
            .ShouldBe([(older.Completed.Session.Value, 9L, Now), (newer.Completed.Session.Value, 4L, Now)]);
        unitOfWork.Commits.ShouldBe(2);
        unitOfWork.Steps.ShouldBe([Queued, InMemoryAnnouncedEvents.Marked, Committed, Queued, InMemoryAnnouncedEvents.Marked, Committed]);
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
        unitOfWork.Steps.ShouldBeEmpty();
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_pass_logs_a_warning_per_completion_it_announces_again_naming_its_stream_and_version()
    {
        var lost = Stored(streamVersion: 12, storedAt: Now.AddMinutes(-3));
        unannounced.Stored.Add(lost);

        await PassAsync(Settings());

        var warning = logger.Collector.GetSnapshot().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(lost.Completed.Session.Value.ToString(), Case.Insensitive);
        warning.Message.ShouldContain("12", Case.Sensitive);
        warning.Message.ShouldNotContain(lost.Completed.Learner.Value.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task A_pass_keys_the_marker_on_the_stream_the_completion_is_stored_in()
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
    public async Task A_completion_that_cannot_be_read_is_skipped_with_a_warning_and_the_others_are_announced()
    {
        var unreadable = Stored(streamVersion: 2, storedAt: Now.AddHours(-6));
        var older = Stored(streamVersion: 9, storedAt: Now.AddHours(-5));
        var newer = Stored(streamVersion: 4, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([unreadable, older, newer]);
        unannounced.Unreadable.Add(unreadable);

        var passed = await PassAsync(Settings());

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBe([older.Completed.ToIntegrationEvent(), newer.Completed.ToIntegrationEvent()]);
        announced.Added.Select(marker => (marker.StreamId, marker.StreamVersion))
            .ShouldBe([(older.Completed.Session.Value, 9L), (newer.Completed.Session.Value, 4L)]);
        unitOfWork.Commits.ShouldBe(2);
        var skipped = logger.Collector.GetSnapshot()
            .Where(record => record.Level == LogLevel.Warning && record.Message.Contains(unreadable.Completed.Session.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();
        skipped.Message.ShouldContain("2", Case.Sensitive);
        skipped.Message.ShouldNotContain(unreadable.Completed.Learner.Value.ToString(), Case.Insensitive);
        skipped.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_batch_of_completions_that_cannot_be_read_holds_up_no_newer_completion()
    {
        // Unreadable events at the old end of the window fill the first page; the pass reads on after the last of them.
        List<StoredContentCompletion> unreadable = [.. Enumerable.Range(1, 3).Select(version => Stored(streamVersion: version, storedAt: Now.AddDays(-6)))];
        var readable = Stored(streamVersion: 7, storedAt: Now.AddMinutes(-10));
        unannounced.Stored.AddRange([.. unreadable, readable]);
        unannounced.Unreadable.UnionWith(unreadable);

        var passed = await PassAsync(Settings(batchSize: "3"));

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Published.ShouldBe([readable.Completed.ToIntegrationEvent()]);
        announced.Added.ShouldHaveSingleItem().StreamVersion.ShouldBe(7);
        unitOfWork.Commits.ShouldBe(1);
        unannounced.Asked.Select(question => question.After).ShouldBe([UnannouncedEvent.BeforeFirst, 3L]);
    }

    [Fact]
    public async Task A_pass_that_reads_past_skipped_completions_still_announces_at_most_a_batch()
    {
        var unreadable = Stored(streamVersion: 1, storedAt: Now.AddHours(-6));
        var first = Stored(streamVersion: 2, storedAt: Now.AddHours(-5));
        var second = Stored(streamVersion: 3, storedAt: Now.AddHours(-4));
        var third = Stored(streamVersion: 4, storedAt: Now.AddHours(-3));
        unannounced.Stored.AddRange([unreadable, first, second, third]);
        unannounced.Unreadable.Add(unreadable);

        await PassAsync(Settings(batchSize: "2"));

        unitOfWork.Published.ShouldBe([first.Completed.ToIntegrationEvent(), second.Completed.ToIntegrationEvent()]);
        unitOfWork.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_pass_reads_at_most_its_most_pages_of_completions()
    {
        List<StoredContentCompletion> unreadable = [.. Enumerable.Range(1, AnnouncementSweepSettings.MostPagesPerPass + 1).Select(version => Stored(streamVersion: version, storedAt: Now.AddDays(-6)))];
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

    private static StoredContentCompletion Stored(long streamVersion, DateTimeOffset storedAt)
    {
        var learner = PersonId.From(Guid.CreateVersion7());
        var version = CourseVersionId.From(Guid.CreateVersion7());
        var completed = new ContentCompleted(
            LearningSessionId.For(learner, version, SessionRun.First),
            learner,
            CourseId.From(Guid.CreateVersion7()),
            version,
            Timestamp.From(storedAt));
        return new StoredContentCompletion(completed, streamVersion, storedAt);
    }

    private static AnnouncementSweepSettings Settings(string? gracePeriod = null, string? lookback = null, string? batchSize = null) =>
        AnnouncementSweepSettings.From(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Learning:AnnouncementSweep:GracePeriod", gracePeriod),
                    new KeyValuePair<string, string?>("Learning:AnnouncementSweep:Lookback", lookback),
                    new KeyValuePair<string, string?>("Learning:AnnouncementSweep:BatchSize", batchSize),
                ])
                .Build(),
            "Learning");

    private Task<Result<Unit, ApiError>> PassAsync(AnnouncementSweepSettings settings) =>
        new ReannounceContentCompletionsHandler(unannounced, announced, unitOfWork, settings, time, logger)
            .HandleAsync(new ReannounceContentCompletions(), TestContext.Current.CancellationToken);
}
