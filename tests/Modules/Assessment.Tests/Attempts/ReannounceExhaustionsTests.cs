using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceExhaustions;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// One pass of Assessment's sweep of locks (#307): every stored ExaminationExhausted that has no marker, because the commit
/// of its announcement never happened, is announced again as the submit would have announced it (AttemptsExhaustedV1, with
/// the course of its examination and every chapter failed since the last unlock), oldest first, each with its marker in a
/// commit of its own. The pass reads the window and pages as the sweep of passes does (#277, #337, #351, #360).
/// </summary>
public class ReannounceExhaustionsTests
{
    private readonly AttemptFixture examination = new();
    private readonly List<string> steps = [];
    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryUnannouncedExaminationEvents<ExaminationExhausted> unannounced = new();
    private readonly FakeLogger<ReannounceExhaustionsHandler> logger = new();
    private readonly RecordingAssessmentUnitOfWork unitOfWork;
    private readonly InMemoryAnnouncedEvents announced;

    public ReannounceExhaustionsTests()
    {
        unitOfWork = new RecordingAssessmentUnitOfWork(steps);
        announced = new InMemoryAnnouncedEvents(steps);
    }

    [Fact]
    public async Task A_pass_announces_an_unannounced_lock_as_the_submit_did_with_its_marker_in_one_commit()
    {
        var lost = await ExhaustedAsync();
        unannounced.Stored.Add(lost);

        var passed = await PassAsync();

        passed.IsSuccess.ShouldBeTrue();
        var announcedLock = unitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<AttemptsExhaustedV1>();
        (announcedLock.SessionId, announcedLock.PersonId, announcedLock.CourseId, announcedLock.CourseVersionId)
            .ShouldBe((examination.Session.Value, examination.Learner.Value, examination.Course.Value, examination.Version.Value));
        announcedLock.FailedChapterIds.ShouldBe([Gloves.Value, Hygiene.Value], ignoreOrder: true);
        var marker = announced.Added.ShouldHaveSingleItem();
        (marker.StreamId, marker.StreamVersion, marker.AnnouncedAt).ShouldBe((examination.Examination.Value, lost.StreamVersion, Now));
        steps.ShouldBe([RecordingAssessmentUnitOfWork.Queued, InMemoryAnnouncedEvents.Marked, RecordingAssessmentUnitOfWork.Committed]);
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(nameof(ExaminationExhausted), Case.Sensitive);
        warning.Message.ShouldContain(examination.Examination.Value.ToString(), Case.Insensitive);
        warning.Message.ShouldNotContain(examination.Learner.Value.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task A_lock_that_cannot_be_read_is_skipped_with_one_warning_and_keeps_no_marker()
    {
        var lost = await ExhaustedAsync();
        unannounced.Stored.Add(lost);
        unannounced.Unreadable.Add(lost);

        var passed = await PassAsync();

        passed.IsSuccess.ShouldBeTrue();
        unitOfWork.Commits.ShouldBe(0);
        announced.Added.ShouldBeEmpty();
        var skipped = logger.Collector.GetSnapshot().Where(record => record.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        skipped.GetStructuredStateValue("Count").ShouldBe("1");
        skipped.GetStructuredStateValue("EventType").ShouldBe(nameof(ExaminationExhausted));
        skipped.GetStructuredStateValue("FirstFailureType").ShouldBe(nameof(JsonException));
    }

    [Fact]
    public async Task A_database_that_fails_while_a_lock_is_loaded_ends_the_pass_with_the_failure()
    {
        var lost = await ExhaustedAsync();
        unannounced.Stored.Add(lost);
        unannounced.Unreachable.Add(lost);

        await Should.ThrowAsync<DatabaseUnavailable>(() => PassAsync());

        unitOfWork.Commits.ShouldBe(0);
        announced.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pass_asks_for_the_window_between_lookback_and_grace_period_and_the_batch_size()
    {
        time.Advance(TimeSpan.FromMinutes(42));
        var now = time.GetUtcNow();

        await PassAsync(Settings(gracePeriod: "00:00:30", lookback: "2.00:00:00", batchSize: "7"));

        var asked = unannounced.Asked.ShouldHaveSingleItem();
        (asked.StoredFrom, asked.StoredUntil, asked.Limit).ShouldBe((now - TimeSpan.FromDays(2), now - TimeSpan.FromSeconds(30), 7));
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

    /// <summary>Three failed attempts through the domain; the examination's ExaminationExhausted at its version, with the examination.</summary>
    private async Task<PlantedExaminationEvent<ExaminationExhausted>> ExhaustedAsync()
    {
        await examination.OpenAsync();
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        await examination.AttemptThroughTheDomainAsync([A], [E]);
        await examination.AttemptThroughTheDomainAsync([D], [B, C]);
        var stream = examination.Stream();
        var exhausted = stream[^1].ShouldBeOfType<ExaminationExhausted>();
        var loaded = (await examination.Store.LoadAsync<Examination>(examination.Examination, Token)).Value;
        return new PlantedExaminationEvent<ExaminationExhausted>(exhausted, loaded, stream.Count, Now.AddMinutes(-10));
    }

    private Task<Result<Unit, ApiError>> PassAsync(AnnouncementSweepSettings? settings = null) =>
        new ReannounceExhaustionsHandler(unannounced, announced, unitOfWork, settings ?? Settings(), time, logger)
            .HandleAsync(new ReannounceExhaustions(), TestContext.Current.CancellationToken);
}
