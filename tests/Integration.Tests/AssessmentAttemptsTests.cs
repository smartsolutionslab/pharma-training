using System.Net;
using System.Text.Json;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentAttempts;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A learner's attempts through the edge (story A1): the examination read by its learning session, an attempt started
/// with the definition's questions and options shuffled, answered, submitted and scored on the server without
/// partial credit, and a pass announced once as <c>TestPassedV1</c> for Learning's session. Only the examination's
/// own learner reaches it; examination ids are derivable, so anyone else is answered as if it did not exist.
/// </summary>
public class AssessmentAttemptsTests(AspireFixture aspire)
{
    private const int ConcurrentStarts = 10;

    [Fact]
    public async Task The_learner_reads_the_examination_by_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var definition = Definition(Guid.CreateVersion7());
        await DeliverAsync(aspire, DefinitionType, definition, cancellationToken);
        var completed = Completed(definition.CourseVersionId, LearningSessions.SubjectOf(client));
        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        using var read = await client.GetAsync(BySession(completed.SessionId), cancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        var examination = await BodyOfAsync<ExaminationBody>(read, cancellationToken);
        examination.Id.ShouldNotBe(Guid.Empty);
        examination.Id.ShouldNotBe(completed.SessionId);
        examination.SessionId.ShouldBe(completed.SessionId);
        examination.CourseId.ShouldBe(completed.CourseId);
        examination.CourseVersionId.ShouldBe(definition.CourseVersionId);
        examination.Status.ShouldBe("Unlocked");
        examination.AttemptsUsed.ShouldBe(0);
        examination.MaxAttempts.ShouldBe(3);
        examination.CurrentAttempt.ShouldBeNull();
        examination.LastResult.ShouldBeNull();
    }

    [Fact]
    public async Task Another_person_gets_404_and_no_token_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        using var other = await aspire.CreateHostClientAsync("employee-b");
        using var anonymous = aspire.CreateHostClient();
        var opened = await OpenExaminationAsync(aspire, learner, cancellationToken);
        var question = opened.Definition.Questions[0];

        foreach (var client in new[] { other, anonymous })
        {
            var expected = client == other ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized;
            using var read = await client.GetAsync(BySession(opened.Completed.SessionId), cancellationToken);
            using var started = await StartAsync(client, opened.ExaminationId, cancellationToken);
            using var answered = await AnswerAsync(client, opened.ExaminationId, question.QuestionId, Right(question), cancellationToken);
            using var submitted = await SubmitAsync(client, opened.ExaminationId, cancellationToken);

            foreach (var response in new[] { read, started, answered, submitted })
            {
                response.StatusCode.ShouldBe(expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}");
                if (client == other)
                {
                    (await EditorialWorkflow.CodeOfAsync(response, cancellationToken)).ShouldBe("assessment.examination-not-found");
                }
            }
        }

        // Nothing anyone else sent reached the learner's examination.
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken)).ShouldBe(["examination_opened"]);
        (await ReadExaminationAsync(learner, opened.Completed.SessionId, cancellationToken)).Status.ShouldBe("Unlocked");
    }

    [Fact]
    public async Task Starting_answers_201_with_the_definitions_questions_and_options_and_stores_that_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);

        using var started = await StartAsync(client, opened.ExaminationId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        started.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/assessment/examinations/by-session/{opened.Completed.SessionId}");
        var attempt = await BodyOfAsync<AttemptBody>(started, cancellationToken);
        attempt.Number.ShouldBe(1);
        attempt.Questions.Select(question => question.Id).ShouldBe(opened.Definition.Questions.Select(question => question.QuestionId), ignoreOrder: true);
        foreach (var question in attempt.Questions)
        {
            var defined = opened.Definition.Questions.Single(candidate => candidate.QuestionId == question.Id);
            question.Type.ShouldBe(defined.Type);
            question.Text.ShouldBe(defined.Text);
            question.Options.ShouldBe([.. defined.Options.Select(option => new OptionBody(option.AnswerOptionId, option.Text))], ignoreOrder: true);
            question.ChosenOptionIds.ShouldBeEmpty();
        }

        (await StoredQuestionOrdersAsync(aspire, opened.ExaminationId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe([.. attempt.Questions.Select(question => question.Id)]);

        using var reload = await client.GetAsync(started.Headers.Location, cancellationToken);
        reload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = (await BodyOfAsync<ExaminationBody>(reload, cancellationToken)).CurrentAttempt.ShouldNotBeNull();
        current.Id.ShouldBe(attempt.Id);
        current.Questions.Select(question => question.Id).ShouldBe(attempt.Questions.Select(question => question.Id));
        foreach (var (shown, again) in attempt.Questions.Zip(current.Questions))
        {
            again.Options.ShouldBe(shown.Options);
        }
    }

    [Fact]
    public async Task A_second_start_while_one_is_open_answers_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        using var first = await StartAsync(client, opened.ExaminationId, cancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(cancellationToken));

        using var second = await StartAsync(client, opened.ExaminationId, cancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EditorialWorkflow.CodeOfAsync(second, cancellationToken)).ShouldBe("assessment.examination-not-unlocked");
        (await StoredQuestionOrdersAsync(aspire, opened.ExaminationId, cancellationToken)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Starting_before_the_definition_arrives_answers_409_test_definition_missing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, withDefinition: false, cancellationToken);

        using var started = await StartAsync(client, opened.ExaminationId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EditorialWorkflow.CodeOfAsync(started, cancellationToken)).ShouldBe("assessment.test-definition-missing");
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken)).ShouldBe(["examination_opened"]);
        (await ReadExaminationAsync(client, opened.Completed.SessionId, cancellationToken)).MaxAttempts.ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_starts_answer_201_once_and_409_otherwise_never_500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentStarts).Select(_ => StartAsync(client, opened.ExaminationId, cancellationToken)));
        try
        {
            var statuses = responses.Select(response => response.StatusCode).ToList();
            statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(1, string.Join(", ", statuses));
            foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(cancellationToken));
                // A start that lost the race on the stream is told it changed; one PostgreSQL ended as a deadlock is told to try again.
                (await EditorialWorkflow.CodeOfAsync(refused, cancellationToken)).ShouldBeOneOf("assessment.examination-not-unlocked", "assessment.examination-changed", "assessment.concurrent-save");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken)).Count(type => type == "attempt_started").ShouldBe(1);
    }

    /// <summary>#48's "a reload keeps the answers": the last answer to each question comes back with the examination.</summary>
    [Fact]
    public async Task Answering_answers_204_and_a_reload_shows_the_chosen_options()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        using (var started = await StartAsync(client, opened.ExaminationId, cancellationToken))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        }

        var single = opened.Definition.Questions[0];
        var multiple = opened.Definition.Questions[1];

        using var wrong = await AnswerAsync(client, opened.ExaminationId, single.QuestionId, Wrong(single), cancellationToken);
        using var right = await AnswerAsync(client, opened.ExaminationId, single.QuestionId, Right(single), cancellationToken);
        using var several = await AnswerAsync(client, opened.ExaminationId, multiple.QuestionId, Right(multiple), cancellationToken);

        wrong.StatusCode.ShouldBe(HttpStatusCode.NoContent, await wrong.Content.ReadAsStringAsync(cancellationToken));
        right.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        several.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var current = (await ReadExaminationAsync(client, opened.Completed.SessionId, cancellationToken)).CurrentAttempt.ShouldNotBeNull();
        current.Questions.Single(question => question.Id == single.QuestionId).ChosenOptionIds.ShouldBe(Right(single));
        current.Questions.Single(question => question.Id == multiple.QuestionId).ChosenOptionIds.ShouldBe(Right(multiple), ignoreOrder: true);
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken))
            .ShouldBe(["examination_opened", "attempt_started", "question_answered", "question_answered", "question_answered"]);
    }

    [Fact]
    public async Task Refusals_answer_their_codes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var single = opened.Definition.Questions[0];
        var multiple = opened.Definition.Questions[1];

        // Before any attempt: no open attempt to answer.
        using (var early = await AnswerAsync(client, opened.ExaminationId, single.QuestionId, Right(single), cancellationToken))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await EditorialWorkflow.CodeOfAsync(early, cancellationToken)).ShouldBe("assessment.no-open-attempt");
        }

        using (var started = await StartAsync(client, opened.ExaminationId, cancellationToken))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        }

        var bothOptions = single.Options.Select(option => option.AnswerOptionId).ToList();
        await ShouldRefuseAsync(single.QuestionId, bothOptions, HttpStatusCode.UnprocessableEntity, "assessment.single-choice-takes-one-option");
        await ShouldRefuseAsync(single.QuestionId, [multiple.Options[0].AnswerOptionId], HttpStatusCode.UnprocessableEntity, "assessment.option-not-in-question");
        await ShouldRefuseAsync(Guid.CreateVersion7(), Right(single), HttpStatusCode.NotFound, "assessment.question-not-in-attempt");
        await ShouldRefuseAsync(single.QuestionId, [], HttpStatusCode.BadRequest, "invalid-argument");
        await ShouldRefuseAsync(multiple.QuestionId, [multiple.Options[0].AnswerOptionId, multiple.Options[0].AnswerOptionId], HttpStatusCode.BadRequest, "invalid-argument");
        await ShouldRefuseAsync(multiple.QuestionId, [Guid.Empty], HttpStatusCode.BadRequest, "invalid-argument");
        await ShouldRefuseAsync(Guid.Empty, Right(single), HttpStatusCode.BadRequest, "invalid-argument");
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken)).ShouldBe(["examination_opened", "attempt_started"]);

        using (var submitted = await SubmitAsync(client, opened.ExaminationId, cancellationToken))
        {
            submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync(cancellationToken));
        }

        await ShouldRefuseAsync(single.QuestionId, Right(single), HttpStatusCode.Conflict, "assessment.no-open-attempt");
        using var again = await SubmitAsync(client, opened.ExaminationId, cancellationToken);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EditorialWorkflow.CodeOfAsync(again, cancellationToken)).ShouldBe("assessment.no-open-attempt");

        async Task ShouldRefuseAsync(Guid questionId, IReadOnlyList<Guid> optionIds, HttpStatusCode status, string code)
        {
            using var refused = await AnswerAsync(client, opened.ExaminationId, questionId, optionIds, cancellationToken);
            refused.StatusCode.ShouldBe(status, $"{questionId} [{string.Join(", ", optionIds)}]: {await refused.Content.ReadAsStringAsync(cancellationToken)}");
            (await EditorialWorkflow.CodeOfAsync(refused, cancellationToken)).ShouldBe(code);
        }
    }

    /// <summary>
    /// Listens on a queue of the test's own, up to a marker examination the same learner passes afterwards: Assessment
    /// sends its events in the order it commits them, so a second announcement of the first pass would come before it.
    /// </summary>
    [Fact]
    public async Task Passing_answers_the_score_and_publishes_one_TestPassedV1_for_learnings_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, PassedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, PassedExchange, cancellationToken);
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var before = TimeProvider.System.GetUtcNow();

        var result = await AttemptAsync(client, opened, Right, cancellationToken);

        var after = TimeProvider.System.GetUtcNow();
        (result.Number, result.ScorePercent, result.PassingScorePercent, result.Outcome, result.AttemptsLeft).ShouldBe((1, 100, 80, "Passed", 0));
        result.ChaptersToRepeat.ShouldBeEmpty();
        (await EventTypesAsync(aspire, opened.ExaminationId, cancellationToken))
            .ShouldBe(["examination_opened", "attempt_started", "question_answered", "question_answered", "attempt_submitted", "examination_passed"]);
        (await ReadExaminationAsync(client, opened.Completed.SessionId, cancellationToken)).Status.ShouldBe("Passed");

        var marker = await OpenExaminationAsync(aspire, client, cancellationToken);
        await AttemptAsync(client, marker, Right, cancellationToken);
        var announced = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, received => SessionOf(received) == marker.Completed.SessionId, cancellationToken);
        var passed = announced.Where(received => SessionOf(received) == opened.Completed.SessionId).ShouldHaveSingleItem();
        passed.GetProperty("attemptId").GetGuid().ShouldBe(result.AttemptId);
        passed.GetProperty("sessionId").GetGuid().ShouldNotBe(opened.ExaminationId);
        passed.GetProperty("personId").GetGuid().ShouldBe(LearningSessions.SubjectOf(client));
        passed.GetProperty("courseId").GetGuid().ShouldBe(opened.Completed.CourseId);
        passed.GetProperty("courseVersionId").GetGuid().ShouldBe(opened.Definition.CourseVersionId);
        passed.GetProperty("scorePercent").GetInt32().ShouldBe(100);
        passed.GetProperty("passedAt").GetDateTimeOffset().ShouldBeInRange(before, after);
    }

    /// <summary>The issue's reference test through the running application: the single choice right, the multiple choice only half right.</summary>
    [Fact]
    public async Task A_partly_right_multiple_choice_answer_earns_no_credit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, PassedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, PassedExchange, cancellationToken);
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var multiple = opened.Definition.Questions[1];

        var result = await AttemptAsync(
            client,
            opened,
            question => question.QuestionId == multiple.QuestionId ? [multiple.Options[0].AnswerOptionId] : Right(question),
            cancellationToken);

        (result.ScorePercent, result.Outcome, result.AttemptsLeft).ShouldBe((50, "Failed", 2));
        result.ChaptersToRepeat.ShouldBe([multiple.ChapterId]);

        var marker = await OpenExaminationAsync(aspire, client, cancellationToken);
        await AttemptAsync(client, marker, Right, cancellationToken);
        var announced = await Broker.ReceiveUntilAsync<JsonElement>(channel, queue, received => SessionOf(received) == marker.Completed.SessionId, cancellationToken);
        announced.ShouldNotContain(received => SessionOf(received) == opened.Completed.SessionId);
    }

    [Fact]
    public async Task A_passed_examination_refuses_another_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        (await AttemptAsync(client, opened, Right, cancellationToken)).Outcome.ShouldBe("Passed");

        using var again = await StartAsync(client, opened.ExaminationId, cancellationToken);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EditorialWorkflow.CodeOfAsync(again, cancellationToken)).ShouldBe("assessment.examination-not-unlocked");
    }

    [Fact]
    public async Task The_third_failure_shows_exhausted_and_refuses_a_fourth_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);
        var single = opened.Definition.Questions[0];
        var multiple = opened.Definition.Questions[1];

        var first = await AttemptAsync(client, opened, question => question.QuestionId == single.QuestionId ? Wrong(question) : Right(question), cancellationToken);
        var second = await AttemptAsync(client, opened, question => question.QuestionId == multiple.QuestionId ? Wrong(question) : Right(question), cancellationToken);
        var third = await AttemptAsync(client, opened, question => question.QuestionId == single.QuestionId ? Wrong(question) : Right(question), cancellationToken);

        (first.Number, first.AttemptsLeft).ShouldBe((1, 2));
        first.ChaptersToRepeat.ShouldBe([single.ChapterId]);
        (second.Number, second.AttemptsLeft).ShouldBe((2, 1));
        second.ChaptersToRepeat.ShouldBe([multiple.ChapterId]);
        (third.Number, third.Outcome, third.AttemptsLeft).ShouldBe((3, "Failed", 0));
        third.ChaptersToRepeat.ShouldBe([single.ChapterId, multiple.ChapterId], ignoreOrder: true);
        var examination = await ReadExaminationAsync(client, opened.Completed.SessionId, cancellationToken);
        examination.Status.ShouldBe("Exhausted");
        examination.AttemptsUsed.ShouldBe(3);

        using var fourth = await StartAsync(client, opened.ExaminationId, cancellationToken);

        fourth.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EditorialWorkflow.CodeOfAsync(fourth, cancellationToken)).ShouldBe("assessment.examination-not-unlocked");
    }

    private static Guid SessionOf(JsonElement received) => received.GetProperty("sessionId").GetGuid();
}
