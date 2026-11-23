using System.Net;
using System.Text.Json;
using RabbitMQ.Client;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What Learning announces on the broker (story L2): a session once when it is created, never when it is resumed, and
/// its content once per cycle when its last chapter is worked through. Each test counts on a queue of its own, up to a
/// marker session announced after the steps it counts: Learning sends the events of one type in the order it commits
/// them, so the marker's comes after any the counted steps would have sent. The learner is made a member of a pharmacy
/// first, since only an active member may start a session (#194).
/// </summary>
public class LearningSessionEventsTests(AspireFixture aspire)
{
    private const string StartedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.LearningSessionStartedV1";
    private const string CompletedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.ContentCompletedV1";

    [Fact]
    public async Task Starting_a_session_sends_one_LearningSessionStartedV1_and_resuming_sends_nothing_more()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await ListenAsync(client, connection, channel, StartedExchange, complete: false, cancellationToken);

        var published = await PublishedToLearningAsync(cancellationToken);
        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        using (var resumed = await StartAsync(client, published.CourseVersionId, cancellationToken))
        {
            resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync(cancellationToken));
        }

        var marker = await StartedSessionAsync(client, cancellationToken);
        var received = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, message => SessionOf(message) == marker.Id, cancellationToken);

        var announced = received.Where(message => SessionOf(message) == session.Id).ShouldHaveSingleItem();
        ShouldName(announced, session.Id, SubjectOf(client), published);
    }

    [Fact]
    public async Task Working_through_every_chapter_sends_one_ContentCompletedV1_and_a_repeated_report_sends_nothing_more()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await ListenAsync(client, connection, channel, CompletedExchange, complete: true, cancellationToken);

        var published = await PublishedToLearningAsync(cancellationToken);
        var session = await StartedSessionAsync(client, published, cancellationToken);
        await CompleteAsync(client, session.Id, published, cancellationToken);
        using (var repeated = await ReportProgressAsync(client, session.Id, ChapterOf(published, "Video"), 95, cancellationToken))
        {
            repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await repeated.Content.ReadAsStringAsync(cancellationToken));
        }

        var marker = await StartedSessionAsync(client, cancellationToken);
        await CompleteAsync(client, marker.Id, marker.Published, cancellationToken);
        var received = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, message => SessionOf(message) == marker.Id, cancellationToken);

        var completed = received.Where(message => SessionOf(message) == session.Id).ShouldHaveSingleItem();
        ShouldName(completed, session.Id, SubjectOf(client), published);
        using var read = await ReadSessionAsync(client, session.Id, cancellationToken);
        (await BodyOfAsync(read, cancellationToken)).Status.ShouldBe("ContentCompleted");
        // The marker orders the queue only up to the marker; the stream shows no later duplicate was stored either.
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'content_completed'", session.Id, cancellationToken))
            .ShouldBe(1);
    }

    /// <summary>Two reports that each complete the last open chapter race; only one append lands, so the content completes, and is announced, once.</summary>
    [Fact]
    public async Task Racing_reports_that_complete_the_content_send_one_ContentCompletedV1()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await ListenAsync(client, connection, channel, CompletedExchange, complete: true, cancellationToken);

        var published = await PublishedToLearningAsync(cancellationToken);
        var session = await StartedSessionAsync(client, published, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Text"), 100, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Pdf"), 100, cancellationToken);
        var film = ChapterOf(published, "Video");
        await Task.Delay(ShortVideoWatched, cancellationToken);

        var racing = await Task.WhenAll(
            ReportProgressAsync(client, session.Id, film, 95, cancellationToken),
            ReportProgressAsync(client, session.Id, film, 100, cancellationToken));
        try
        {
            // A racer that loads after the other's report landed sees the session clock at that report (ADR 0024), so its
            // video gain may be refused as too fast; one of the two still lands, and the content completes once.
            foreach (var response in racing)
            {
                response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity], await response.Content.ReadAsStringAsync(cancellationToken));
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    (await EditorialWorkflow.CodeOfAsync(response, cancellationToken)).ShouldBe("learning.progress-too-fast");
                }
            }

            racing.ShouldContain(response => response.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            foreach (var response in racing)
            {
                response.Dispose();
            }
        }

        var marker = await StartedSessionAsync(client, cancellationToken);
        await CompleteAsync(client, marker.Id, marker.Published, cancellationToken);
        var received = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, message => SessionOf(message) == marker.Id, cancellationToken);

        received.Where(message => SessionOf(message) == session.Id).ShouldHaveSingleItem();
        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'content_completed'", session.Id, cancellationToken))
            .ShouldBe(1);
    }

    /// <summary>
    /// Binds a queue of the test's own to the exchange. A session of the listener's own goes through the step that
    /// announces first, so Learning has sent the event and Wolverine has declared its exchange, which no consumer of
    /// this application declares before #45.
    /// </summary>
    private async Task<string> ListenAsync(HttpClient client, IConnection connection, IChannel channel, string exchange, bool complete, CancellationToken cancellationToken)
    {
        var warmUp = await StartedSessionAsync(client, cancellationToken);
        if (complete)
        {
            await CompleteAsync(client, warmUp.Id, warmUp.Published, cancellationToken);
        }

        await Broker.WaitForExchangeAsync(connection, exchange, cancellationToken);
        return await Broker.BindQueueAsync(channel, exchange, cancellationToken);
    }

    private async Task<StartedSession> StartedSessionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var published = await PublishedToLearningAsync(cancellationToken);
        var session = await StartedSessionAsync(client, published, cancellationToken);
        return new StartedSession(session.Id, published);
    }

    private static async Task<SessionBody> StartedSessionAsync(HttpClient client, PublishedEvent published, CancellationToken cancellationToken)
    {
        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync(started, cancellationToken);
    }

    /// <summary>Works through every chapter: text and PDF to the end, the video to 95 % once its playback time has passed.</summary>
    private static async Task CompleteAsync(HttpClient client, Guid session, PublishedEvent published, CancellationToken cancellationToken)
    {
        await ReportOkAsync(client, session, ChapterOf(published, "Text"), 100, cancellationToken);
        await ReportOkAsync(client, session, ChapterOf(published, "Pdf"), 100, cancellationToken);
        await Task.Delay(ShortVideoWatched, cancellationToken);
        await ReportOkAsync(client, session, ChapterOf(published, "Video"), 95, cancellationToken);
    }

    private static async Task ReportOkAsync(HttpClient client, Guid session, Guid chapter, int percent, CancellationToken cancellationToken)
    {
        using var reported = await ReportProgressAsync(client, session, chapter, percent, cancellationToken);
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(cancellationToken));
    }

    private static void ShouldName(JsonElement message, Guid session, Guid person, PublishedEvent published)
    {
        message.GetProperty("sessionId").GetGuid().ShouldBe(session);
        message.GetProperty("personId").GetGuid().ShouldBe(person);
        message.GetProperty("courseId").GetGuid().ShouldBe(published.CourseId);
        message.GetProperty("courseVersionId").GetGuid().ShouldBe(published.CourseVersionId);
        message.GetProperty("metadata").GetProperty("eventIdentifier").GetGuid().ShouldNotBe(Guid.Empty);
    }

    private static Guid SessionOf(JsonElement message) => message.GetProperty("sessionId").GetGuid();

    private static Guid ChapterOf(PublishedEvent published, string kind) => published.Chapters.Single(chapter => chapter.Kind == kind).ChapterId;

    /// <summary>Every version here has a <see cref="ShortVideoSeconds"/> video, so working through it takes a second, not the 420 s a real film would.</summary>
    private async Task<PublishedEvent> PublishedToLearningAsync(CancellationToken cancellationToken)
    {
        var published = Published(Guid.CreateVersion7(), ShortVideoSeconds);
        await DeliverAsync(aspire, published, cancellationToken);
        return published;
    }

    private sealed record StartedSession(Guid Id, PublishedEvent Published);
}
