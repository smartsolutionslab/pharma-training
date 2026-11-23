using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Assessment's side of the tests: Editorial's <c>TestDefinitionPublishedV1</c> and Learning's <c>ContentCompletedV1</c>
/// sent as their Wolverine sends them, the barrier that Assessment's inbox handled them, and Assessment's own tables
/// and event store read directly.
/// </summary>
internal static class AssessmentDatabase
{
    public const string DefinitionType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.TestDefinitionPublishedV1";

    public const string CompletedType = "SmartSolutionsLab.PharmaTraining.Contracts.Learning.ContentCompletedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>The test of a new course version: a single choice question, then a multiple choice one, in the order Editorial lists them.</summary>
    public static SentDefinition Definition(Guid courseVersionId) =>
        new(
            Guid.CreateVersion7(),
            courseVersionId,
            80,
            3,
            [
                new SentQuestion(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "SingleChoice",
                    "Ist Händewaschen vor dem Herstellen Pflicht?",
                    [new SentOption(Guid.CreateVersion7(), "Ja", true), new SentOption(Guid.CreateVersion7(), "Nein", false)]),
                new SentQuestion(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "MultipleChoice",
                    "Was gehört zur Schutzkleidung?",
                    [new SentOption(Guid.CreateVersion7(), "Handschuhe", true), new SentOption(Guid.CreateVersion7(), "Kittel", true), new SentOption(Guid.CreateVersion7(), "Schmuck", false)]),
            ],
            NewMetadata());

    /// <summary>A learner completed the content of a new session of a course version.</summary>
    public static SentCompletion Completed(Guid courseVersionId) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), courseVersionId, NewMetadata());

    /// <summary>The person completed the content of a new session of a course version: their examination opens.</summary>
    public static SentCompletion Completed(Guid courseVersionId, Guid personId) =>
        new(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), courseVersionId, NewMetadata());

    /// <summary>Sends the event under a new envelope id and waits until Assessment's inbox has handled it.</summary>
    public static async Task DeliverAsync<T>(AspireFixture aspire, string type, T message, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await Broker.SendAsync(aspire, type, message, messageId, cancellationToken);
        await WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    /// <summary>
    /// The barrier: Assessment's durable inbox marks the envelope handled once its consumer is done with it. A later
    /// message is no barrier, since a listener handles its messages in parallel.
    /// </summary>
    public static async Task WaitUntilHandledAsync(AspireFixture aspire, Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            var status = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (status == "Handled")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Assessment's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public static async Task<long> CountAsync(AspireFixture aspire, string query, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task<NpgsqlConnection> OpenAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("assessment-database", cancellationToken)
            ?? throw new InvalidOperationException("Assessment's database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    public sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    public sealed record SentOption(Guid AnswerOptionId, string Text, bool IsCorrect);

    public sealed record SentQuestion(Guid QuestionId, Guid ChapterId, string Type, string Text, IReadOnlyList<SentOption> Options);

    public sealed record SentDefinition(Guid CourseId, Guid CourseVersionId, int PassingScorePercent, int MaxAttempts, IReadOnlyList<SentQuestion> Questions, EventMetadata Metadata);

    public sealed record SentCompletion(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);
}
