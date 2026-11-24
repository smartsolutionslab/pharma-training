using System.Net;
using System.Net.Http.Json;
using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A learner's examination through the edge, as their client works on it: read by the learning session, then an
/// attempt started, answered and submitted by the examination's id. The examination is opened as the running
/// application opens it: Editorial's test definition sent as <c>editorial</c>, Learning's completion as <c>learning</c>.
/// </summary>
internal static class AssessmentAttempts
{
    public const string PassedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Assessment.TestPassedV1";

    public static Uri BySession(Guid sessionId) => new($"/assessment/examinations/by-session/{sessionId}", UriKind.Relative);

    public static Uri Attempts(Guid examinationId) => new($"/assessment/examinations/{examinationId}/attempts", UriKind.Relative);

    public static Uri Answer(Guid examinationId, Guid questionId) =>
        new($"/assessment/examinations/{examinationId}/attempts/current/answers/{questionId}", UriKind.Relative);

    public static Uri Submit(Guid examinationId) => new($"/assessment/examinations/{examinationId}/attempts/current/submit", UriKind.Relative);

    /// <summary>Opens the examination of a new session of the client's person on a new version whose test definition arrived first.</summary>
    public static Task<Opened> OpenExaminationAsync(AspireFixture aspire, HttpClient client, CancellationToken cancellationToken) =>
        OpenExaminationAsync(aspire, client, withDefinition: true, cancellationToken);

    /// <summary>
    /// Opens the examination of a new session of the client's person on a new version, whose test definition arrived
    /// first, unless <paramref name="withDefinition"/> says it has not; then reads it by the session, as the client does.
    /// </summary>
    public static async Task<Opened> OpenExaminationAsync(AspireFixture aspire, HttpClient client, bool withDefinition, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        var definition = Definition(Guid.CreateVersion7());
        if (withDefinition)
        {
            await DeliverAsync(aspire, DefinitionType, definition, cancellationToken);
        }

        var completed = Completed(definition.CourseVersionId, LearningSessions.SubjectOf(client));
        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        using var read = await client.GetAsync(BySession(completed.SessionId), cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        var examination = await BodyOfAsync<ExaminationBody>(read, cancellationToken);
        return new Opened(definition, completed, examination.Id);
    }

    public static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid examinationId, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PostAsync(Attempts(examinationId), content: null, cancellationToken);
    }

    public static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid examinationId, Guid questionId, IReadOnlyList<Guid> optionIds, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PutAsJsonAsync(Answer(examinationId, questionId), new { optionIds }, cancellationToken);
    }

    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid examinationId, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PostAsync(Submit(examinationId), content: null, cancellationToken);
    }

    public static async Task<ExaminationBody> ReadExaminationAsync(HttpClient client, Guid sessionId, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        using var read = await client.GetAsync(BySession(sessionId), cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync<ExaminationBody>(read, cancellationToken);
    }

    /// <summary>Starts an attempt, answers each question with the options <paramref name="choose"/> picks from the definition's question, and submits it; the submit's result.</summary>
    public static async Task<ResultBody> AttemptAsync(HttpClient client, Opened opened, Func<SentQuestion, IReadOnlyList<Guid>> choose, CancellationToken cancellationToken)
    {
        Ensure.That(opened).IsNotNull();
        Ensure.That(choose).IsNotNull();

        using (var started = await StartAsync(client, opened.ExaminationId, cancellationToken))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        }

        foreach (var question in opened.Definition.Questions)
        {
            using var answered = await AnswerAsync(client, opened.ExaminationId, question.QuestionId, choose(question), cancellationToken);
            answered.StatusCode.ShouldBe(HttpStatusCode.NoContent, await answered.Content.ReadAsStringAsync(cancellationToken));
        }

        using var submitted = await SubmitAsync(client, opened.ExaminationId, cancellationToken);
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync<ResultBody>(submitted, cancellationToken);
    }

    /// <summary>Exactly the question's right options.</summary>
    public static IReadOnlyList<Guid> Right(SentQuestion question)
    {
        Ensure.That(question).IsNotNull();
        return [.. question.Options.Where(option => option.IsCorrect).Select(option => option.AnswerOptionId)];
    }

    /// <summary>One wrong option of the question.</summary>
    public static IReadOnlyList<Guid> Wrong(SentQuestion question)
    {
        Ensure.That(question).IsNotNull();
        return [question.Options.First(option => !option.IsCorrect).AnswerOptionId];
    }

    public static async Task<T> BodyOfAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        Ensure.That(response).IsNotNull();

        var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        body.ShouldNotBeNull();
        return body;
    }

    /// <summary>The types of the examination's stored events, in the order they were appended.</summary>
    public static async Task<List<string>> EventTypesAsync(AspireFixture aspire, Guid examinationId, CancellationToken cancellationToken)
    {
        await using var connection = await AssessmentDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select type from assessment.mt_events where stream_id = @stream order by version", connection);
        command.Parameters.AddWithValue("stream", examinationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> types = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            types.Add(reader.GetString(0));
        }

        return types;
    }

    /// <summary>The question order each stored <c>AttemptStarted</c> of the examination holds, oldest first; Marten writes the event's members as they are named.</summary>
    public static async Task<List<List<Guid>>> StoredQuestionOrdersAsync(AspireFixture aspire, Guid examinationId, CancellationToken cancellationToken)
    {
        await using var connection = await AssessmentDatabase.OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select coalesce(data->'Questions', data->'questions')::text from assessment.mt_events where stream_id = @stream and type = 'attempt_started' order by version",
            connection);
        command.Parameters.AddWithValue("stream", examinationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<List<Guid>> orders = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(reader.GetString(0)) ?? []);
        }

        return orders;
    }

    public sealed record Opened(SentDefinition Definition, SentCompletion Completed, Guid ExaminationId);

    public sealed record ExaminationBody(
        Guid Id,
        Guid SessionId,
        Guid CourseId,
        Guid CourseVersionId,
        string Status,
        int AttemptsUsed,
        int? MaxAttempts,
        AttemptBody? CurrentAttempt,
        ResultBody? LastResult);

    public sealed record AttemptBody(Guid Id, int Number, IReadOnlyList<QuestionBody> Questions);

    public sealed record QuestionBody(Guid Id, string Type, string Text, IReadOnlyList<OptionBody> Options, IReadOnlyList<Guid> ChosenOptionIds);

    public sealed record OptionBody(Guid Id, string Text);

    public sealed record ResultBody(Guid AttemptId, int Number, int ScorePercent, int PassingScorePercent, string Outcome, IReadOnlyList<Guid> ChaptersToRepeat, int AttemptsLeft);
}
