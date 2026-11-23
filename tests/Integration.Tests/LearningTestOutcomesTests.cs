using System.Net;
using System.Text.Json;
using RabbitMQ.Client;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning reacts to the test's outcome (#42): Assessment's <c>AttemptsExhaustedV1</c> reopens the chapters of the wrong
/// answers, once per announcement, and its <c>TestPassedV1</c> finishes the session, which no lock changes afterwards.
/// Each event is sent as Assessment's Wolverine sends it, with Assessment's broker user (docs/adr/0011), and the test
/// waits until Learning's inbox has handled it.
/// </summary>
public class LearningTestOutcomesTests(AspireFixture aspire)
{
    private const string ExhaustedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.AttemptsExhaustedV1";
    private const string PassedType = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestPassedV1";
    private const string CompletedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.ContentCompletedV1";

    [Fact]
    public async Task Exhausted_attempts_reopen_the_failed_chapters_and_working_them_through_again_sends_a_second_ContentCompletedV1()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await ListenToCompletionsAsync(client, connection, channel, cancellationToken);
        var session = await CompletedSessionAsync(client, cancellationToken);
        var pdf = ChapterOf(session.Published, "Pdf");

        await SendAsync(ExhaustedType, Exhausted(client, session, Guid.CreateVersion7(), pdf), cancellationToken);

        var reopened = await ReadAsync(client, session.Id, cancellationToken);
        reopened.Status.ShouldBe("InProgress");
        var chapters = reopened.Chapters.ToDictionary(chapter => chapter.ChapterId);
        (chapters[pdf].Completed, chapters[pdf].ProgressPercent).ShouldBe((false, 0));
        (chapters[ChapterOf(session.Published, "Text")].Completed, chapters[ChapterOf(session.Published, "Text")].ProgressPercent).ShouldBe((true, 100));
        (chapters[ChapterOf(session.Published, "Video")].Completed, chapters[ChapterOf(session.Published, "Video")].ProgressPercent).ShouldBe((true, 95));

        await ReportOkAsync(client, session.Id, pdf, 100, cancellationToken);
        (await ReadAsync(client, session.Id, cancellationToken)).Status.ShouldBe("ContentCompleted");

        var marker = await CompletedSessionAsync(client, cancellationToken);
        var received = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, message => SessionOf(message) == marker.Id, cancellationToken);
        received.Count(message => SessionOf(message) == session.Id).ShouldBe(2, "the content was not announced complete a second time after the reopened chapter was worked through");
    }

    [Fact]
    public async Task The_same_exhaustion_sent_again_under_a_new_envelope_after_the_chapters_were_worked_through_reopens_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        var session = await CompletedSessionAsync(client, cancellationToken);
        var pdf = ChapterOf(session.Published, "Pdf");
        var exhausted = Exhausted(client, session, Guid.CreateVersion7(), pdf);
        await SendAsync(ExhaustedType, exhausted, cancellationToken);
        await ReportOkAsync(client, session.Id, pdf, 100, cancellationToken);

        await SendAsync(ExhaustedType, exhausted, cancellationToken);

        (await ReadAsync(client, session.Id, cancellationToken)).Status.ShouldBe("ContentCompleted");
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'chapters_reopened'", session.Id, cancellationToken))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_passed_test_finishes_the_session_and_further_progress_answers_409_session_finished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        var session = await CompletedSessionAsync(client, cancellationToken);

        await SendAsync(PassedType, Passed(client, session), cancellationToken);

        (await ReadAsync(client, session.Id, cancellationToken)).Status.ShouldBe("Finished");
        using var reported = await ReportProgressAsync(client, session.Id, ChapterOf(session.Published, "Video"), 100, cancellationToken);
        reported.StatusCode.ShouldBe(HttpStatusCode.Conflict, await reported.Content.ReadAsStringAsync(cancellationToken));
        (await EditorialWorkflow.CodeOfAsync(reported, cancellationToken)).ShouldBe("learning.session-finished");
    }

    [Fact]
    public async Task A_lock_after_passing_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        var session = await CompletedSessionAsync(client, cancellationToken);
        await SendAsync(PassedType, Passed(client, session), cancellationToken);

        await SendAsync(ExhaustedType, Exhausted(client, session, Guid.CreateVersion7(), ChapterOf(session.Published, "Pdf")), cancellationToken);

        var read = await ReadAsync(client, session.Id, cancellationToken);
        read.Status.ShouldBe("Finished");
        read.Chapters.ShouldAllBe(chapter => chapter.Completed);
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'session_finished'", session.Id, cancellationToken))
            .ShouldBe(1);
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'chapters_reopened'", session.Id, cancellationToken))
            .ShouldBe(0);
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'exhaustion_noted'", session.Id, cancellationToken))
            .ShouldBe(0);
    }

    /// <summary>Sends the event as Assessment under a new envelope id and waits until Learning's inbox has handled it.</summary>
    private async Task SendAsync<T>(string type, T message, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await Broker.SendAsync(aspire, type, message, messageId, cancellationToken);
        await WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    /// <summary>Assessment's <c>AttemptsExhaustedV1</c> as camelCase JSON, its event identifier named, so a resend carries the same one.</summary>
    private static object Exhausted(HttpClient client, CompletedSession session, Guid eventIdentifier, params Guid[] failedChapters) =>
        new
        {
            sessionId = session.Id,
            personId = SubjectOf(client),
            courseId = session.Published.CourseId,
            courseVersionId = session.Published.CourseVersionId,
            failedChapterIds = failedChapters,
            metadata = new { eventIdentifier, occurredAt = TimeProvider.System.GetUtcNow() },
        };

    private static object Passed(HttpClient client, CompletedSession session) =>
        new
        {
            attemptId = Guid.CreateVersion7(),
            sessionId = session.Id,
            personId = SubjectOf(client),
            courseId = session.Published.CourseId,
            courseVersionId = session.Published.CourseVersionId,
            scorePercent = 90,
            passedAt = TimeProvider.System.GetUtcNow().AddMinutes(-1),
            metadata = new { eventIdentifier = Guid.CreateVersion7(), occurredAt = TimeProvider.System.GetUtcNow() },
        };

    /// <summary>
    /// Binds a queue of the test's own to Learning's <c>ContentCompletedV1</c>, once a session of the listener's own has
    /// completed, so Learning has sent the event and Wolverine has declared its exchange.
    /// </summary>
    private async Task<string> ListenToCompletionsAsync(HttpClient client, IConnection connection, IChannel channel, CancellationToken cancellationToken)
    {
        await CompletedSessionAsync(client, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CompletedExchange, cancellationToken);
        return await Broker.BindQueueAsync(channel, CompletedExchange, cancellationToken);
    }

    /// <summary>A session of a newly published version that the learner worked through: text and PDF to the end, the video to 95 %.</summary>
    private async Task<CompletedSession> CompletedSessionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);
        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Text"), 100, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Pdf"), 100, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Video"), 95, cancellationToken);
        (await ReadAsync(client, session.Id, cancellationToken)).Status.ShouldBe("ContentCompleted");
        return new CompletedSession(session.Id, published);
    }

    private static async Task<SessionBody> ReadAsync(HttpClient client, Guid session, CancellationToken cancellationToken)
    {
        using var read = await ReadSessionAsync(client, session, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync(read, cancellationToken);
    }

    private static async Task ReportOkAsync(HttpClient client, Guid session, Guid chapter, int percent, CancellationToken cancellationToken)
    {
        using var reported = await ReportProgressAsync(client, session, chapter, percent, cancellationToken);
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(cancellationToken));
    }

    private static Guid SessionOf(JsonElement message) => message.GetProperty("sessionId").GetGuid();

    private static Guid ChapterOf(PublishedEvent published, string kind) => published.Chapters.Single(chapter => chapter.Kind == kind).ChapterId;

    private sealed record CompletedSession(Guid Id, PublishedEvent Published);
}
