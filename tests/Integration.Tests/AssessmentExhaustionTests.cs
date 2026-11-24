using System.Net;
using System.Text.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentAttempts;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The lock and unlock through the running application (story A3): each failed submit through the edge is announced as
/// <c>TestFailedV1</c>, the third also as <c>AttemptsExhaustedV1</c> with the chapters of every wrong answer since
/// the last unlock, and Learning's next <c>ContentCompletedV1</c> grants three new attempts.
/// </summary>
public class AssessmentExhaustionTests(AspireFixture aspire)
{
    private const string FailedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestFailedV1";

    private const string ExhaustedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.AttemptsExhaustedV1";

    [Fact]
    public async Task Three_failures_announce_each_failure_and_the_lock_and_a_new_completion_unlocks_three_attempts()
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
        var single = opened.Definition.Questions[0];
        var multiple = opened.Definition.Questions[1];
        var first = await AttemptAsync(client, opened, question => question.QuestionId == multiple.QuestionId ? Wrong(question) : Right(question), cancellationToken);
        var second = await AttemptAsync(client, opened, question => question.QuestionId == single.QuestionId ? Wrong(question) : Right(question), cancellationToken);
        var third = await AttemptAsync(client, opened, Wrong, cancellationToken);

        third.Outcome.ShouldBe("Failed");
        var session = opened.Completed.SessionId;
        var failures = (await Broker.ReceiveUntilAsync<JsonElement>(channel, failedQueue, received => AttemptOf(received) == third.AttemptId, cancellationToken))
            .Where(received => SessionOf(received) == session)
            .ToList();
        failures.Select(AttemptOf).ShouldBe([first.AttemptId, second.AttemptId, third.AttemptId]);
        failures.Select(received => received.GetProperty("attemptNumber").GetInt32()).ShouldBe([1, 2, 3]);
        foreach (var failure in failures)
        {
            failure.GetProperty("personId").GetGuid().ShouldBe(LearningSessions.SubjectOf(client));
            failure.GetProperty("courseId").GetGuid().ShouldBe(opened.Completed.CourseId);
            failure.GetProperty("courseVersionId").GetGuid().ShouldBe(opened.Definition.CourseVersionId);
        }

        var exhausted = await Broker.ReceiveAsync<JsonElement>(channel, exhaustedQueue, received => SessionOf(received) == session, cancellationToken);
        exhausted.GetProperty("personId").GetGuid().ShouldBe(LearningSessions.SubjectOf(client));
        exhausted.GetProperty("courseId").GetGuid().ShouldBe(opened.Completed.CourseId);
        exhausted.GetProperty("courseVersionId").GetGuid().ShouldBe(opened.Definition.CourseVersionId);
        exhausted.GetProperty("failedChapterIds").EnumerateArray().Select(chapter => chapter.GetGuid())
            .ShouldBe([multiple.ChapterId, single.ChapterId]);
        using (var locked = await StartAsync(client, opened.ExaminationId, cancellationToken))
        {
            locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        await DeliverAsync(aspire, CompletedType, opened.Completed with { Metadata = new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()) }, cancellationToken);

        using var restarted = await StartAsync(client, opened.ExaminationId, cancellationToken);
        restarted.StatusCode.ShouldBe(HttpStatusCode.Created, await restarted.Content.ReadAsStringAsync(cancellationToken));
        var examination = await ReadExaminationAsync(client, session, cancellationToken);
        examination.Status.ShouldBe("InAttempt");
        examination.CurrentAttempt.ShouldNotBeNull().Number.ShouldBe(1);
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken))
            .ShouldBe([
                "examination_opened",
                "attempt_started", "question_answered", "question_answered", "attempt_submitted",
                "attempt_started", "question_answered", "question_answered", "attempt_submitted",
                "attempt_started", "question_answered", "question_answered", "attempt_submitted", "examination_exhausted",
                "examination_unlocked", "attempt_started",
            ]);
    }

    private static Guid SessionOf(JsonElement received) => received.GetProperty("sessionId").GetGuid();

    private static Guid AttemptOf(JsonElement received) => received.GetProperty("attemptId").GetGuid();
}
