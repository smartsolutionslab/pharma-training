using System.Net;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentAttempts;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A failure and a lock that Assessment stored but whose announcement died after the append (#307) are announced by its
/// sweeps of failures and locks, once each: the submit's outbox commit is killed in the running application (a trigger
/// on <c>announced_events</c> that refuses the markers of one examination, so the commit that carries the markers and the
/// envelopes rolls back after the append landed), and the learner would otherwise stay locked without anyone hearing of
/// it. The sweeps announce each event with its marker in the same commit, so a later pass finds the marker and announces
/// nothing again: Learning, which notes each lock by its EventIdentifier (docs/adr/0025), and Compliance hear of it once.
/// </summary>
public class AssessmentLockAnnouncementSweepTests(AspireFixture aspire)
{
    private const string FailedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestFailedV1";

    private const string ExhaustedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.AttemptsExhaustedV1";

    /// <summary>How long a test watches for a second announcement after the first: several passes of every sweep.</summary>
    private static readonly TimeSpan Watched = AspireFixture.AnnouncementSweepInterval * 5;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task A_lock_whose_announcement_died_after_the_append_is_announced_once_by_the_sweeps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);

        // Nothing consumes TestFailedV1, so its exchange exists only once Assessment first sent to it.
        var warmUp = await OpenExaminationAsync(aspire, client, cancellationToken);
        await AttemptAsync(client, warmUp, Wrong, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, FailedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, ExhaustedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var failedQueue = await Broker.BindQueueAsync(channel, FailedExchange, cancellationToken);
        var exhaustedQueue = await Broker.BindQueueAsync(channel, ExhaustedExchange, cancellationToken);

        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var session = opened.Completed.SessionId;
        var first = await AttemptAsync(client, opened, Wrong, cancellationToken);
        var second = await AttemptAsync(client, opened, Wrong, cancellationToken);
        var third = await StartAndAnswerAsync(client, opened, Wrong, cancellationToken);

        // The append lands; the commit of the announcements and their markers is refused, so nothing is announced.
        await using (await KilledAnnouncementsAsync(opened.ExaminationId, cancellationToken))
        {
            using var submitted = await SubmitAsync(client, opened.ExaminationId, cancellationToken);
            submitted.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, await submitted.Content.ReadAsStringAsync(cancellationToken));
        }

        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken))[^2..].ShouldBe(["attempt_submitted", "examination_exhausted"]);
        var exhaustedVersion = await StoredVersionAsync(aspire, opened.ExaminationId, "examination_exhausted", cancellationToken);
        var failedVersion = exhaustedVersion - 1;
        (await CountAnnouncedAsync(aspire, opened.ExaminationId, failedVersion, cancellationToken)).ShouldBe(0, "the killed commit stored the failure's marker");
        (await CountAnnouncedAsync(aspire, opened.ExaminationId, exhaustedVersion, cancellationToken)).ShouldBe(0, "the killed commit stored the lock's marker");

        var failures = (await Broker.ReceiveUntilAsync<JsonElement>(channel, failedQueue, received => AttemptOf(received) == third, cancellationToken))
            .Where(received => SessionOf(received) == session)
            .ToList();
        failures.Select(AttemptOf).ShouldBe([first.AttemptId, second.AttemptId, third]);
        failures[^1].GetProperty("attemptNumber").GetInt32().ShouldBe(3);
        var exhausted = await Broker.ReceiveAsync<JsonElement>(channel, exhaustedQueue, received => SessionOf(received) == session, cancellationToken);
        exhausted.GetProperty("personId").GetGuid().ShouldBe(LearningSessions.SubjectOf(client));
        exhausted.GetProperty("courseId").GetGuid().ShouldBe(opened.Completed.CourseId);
        exhausted.GetProperty("courseVersionId").GetGuid().ShouldBe(opened.Definition.CourseVersionId);
        exhausted.GetProperty("failedChapterIds").EnumerateArray().Select(chapter => chapter.GetGuid())
            .ShouldBe(opened.Definition.Questions.Select(question => question.ChapterId).Distinct(), ignoreOrder: true);

        // Each announcement left with its marker, so no later pass of any replica's sweep announces it again.
        (await CountAnnouncedAsync(aspire, opened.ExaminationId, failedVersion, cancellationToken)).ShouldBe(1);
        (await CountAnnouncedAsync(aspire, opened.ExaminationId, exhaustedVersion, cancellationToken)).ShouldBe(1);
        (await ReceivedWhileWatchingAsync(channel, failedQueue, cancellationToken)).Where(received => SessionOf(received) == session).ShouldBeEmpty();
        (await ReceivedWhileWatchingAsync(channel, exhaustedQueue, cancellationToken)).Where(received => SessionOf(received) == session).ShouldBeEmpty();
        using var locked = await StartAsync(client, opened.ExaminationId, cancellationToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_failing_submit_and_a_lock_record_their_announcements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await AttemptAsync(client, opened, Wrong, cancellationToken)).Outcome.ShouldBe("Failed");
        }

        var exhaustedVersion = await StoredVersionAsync(aspire, opened.ExaminationId, "examination_exhausted", cancellationToken);
        var types = await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken);
        var submittedVersions = types.Select((type, index) => (Type: type, Version: index + 1L)).Where(stored => stored.Type == "attempt_submitted").Select(stored => stored.Version).ToList();
        submittedVersions.Count.ShouldBe(3);
        foreach (var version in submittedVersions.Append(exhaustedVersion))
        {
            (await CountAnnouncedAsync(aspire, opened.ExaminationId, version, cancellationToken)).ShouldBe(1, $"the submit's commit did not record the announcement of version {version}");
        }
    }

    private static Guid SessionOf(JsonElement received) => received.GetProperty("sessionId").GetGuid();

    private static Guid AttemptOf(JsonElement received) => received.GetProperty("attemptId").GetGuid();

    /// <summary>Starts an attempt and answers each question as <paramref name="choose"/> picks, without submitting it; the attempt's id.</summary>
    private static async Task<Guid> StartAndAnswerAsync(HttpClient client, Opened opened, Func<SentQuestion, IReadOnlyList<Guid>> choose, CancellationToken cancellationToken)
    {
        using (var started = await StartAsync(client, opened.ExaminationId, cancellationToken))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        }

        foreach (var question in opened.Definition.Questions)
        {
            using var answered = await AnswerAsync(client, opened.ExaminationId, question.QuestionId, choose(question), cancellationToken);
            answered.StatusCode.ShouldBe(HttpStatusCode.NoContent, await answered.Content.ReadAsStringAsync(cancellationToken));
        }

        var examination = await ReadExaminationAsync(client, opened.Completed.SessionId, cancellationToken);
        return examination.CurrentAttempt.ShouldNotBeNull().Id;
    }

    /// <summary>Every message the queue receives while the test watches it, for several passes of every sweep.</summary>
    private static async Task<List<JsonElement>> ReceivedWhileWatchingAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        List<JsonElement> received = [];
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < Watched)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true, cancellationToken);
            if (delivery is null)
            {
                await Task.Delay(PollInterval, cancellationToken);
                continue;
            }

            received.Add(JsonSerializer.Deserialize<JsonElement>(delivery.Body.Span, JsonSerializerOptions.Web));
        }

        return received;
    }

    /// <summary>
    /// Refuses every marker of the examination's stream in <c>assessment.announced_events</c> until disposed: the commit that
    /// carries them, with the announcements' envelopes, rolls back, as a crash between the append and the outbox commit
    /// leaves it. Only that stream is refused, so the other tests of the run go on as before.
    /// </summary>
    private async Task<IAsyncDisposable> KilledAnnouncementsAsync(Guid examination, CancellationToken cancellationToken)
    {
        var name = $"kill_announcements_{examination:N}";
        await ExecuteAsync(
            $"""
            create function assessment.{name}() returns trigger language plpgsql as $$
            begin
              if new.stream_id = '{examination}' then
                raise exception 'The announcement of stream % was killed after its append (#307).', new.stream_id;
              end if;
              return new;
            end $$;
            create trigger {name} before insert on assessment.announced_events for each row execute function assessment.{name}();
            """,
            cancellationToken);
        return new Revived(() => ExecuteAsync($"drop trigger {name} on assessment.announced_events; drop function assessment.{name}();", CancellationToken.None));
    }

    private async Task ExecuteAsync(string statement, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class Revived(Func<Task> revive) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await revive();
    }
}
