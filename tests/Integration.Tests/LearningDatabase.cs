using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's side of the tests: Editorial's <c>CourseVersionPublishedV1</c> sent as Editorial's Wolverine sends it,
/// the barrier that Learning's inbox handled it, and Learning's own tables read directly.
/// </summary>
internal static class LearningDatabase
{
    public const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The length of a video a test reports on at once: video progress may not outpace playback (#43, ADR 0021), and
    /// a 1 s video is plausibly watched to 100 % once 800 ms have passed since the session's last recorded progress.
    /// </summary>
    public const int ShortVideoSeconds = 1;

    /// <summary>
    /// How long a test waits before reporting progress on a <see cref="ShortVideoSeconds"/> video. The wait is
    /// deterministic, not a hope: the server recorded the session's last progress before it answered, and the next
    /// report reaches it after the wait, on the same clock.
    /// </summary>
    public static readonly TimeSpan ShortVideoWatched = TimeSpan.FromSeconds(1);

    /// <summary>A version of a new course with one text, one PDF and one video chapter of <paramref name="videoSeconds"/>, listed out of reading order.</summary>
    public static PublishedEvent Published(Guid courseVersionId, int videoSeconds = 420) =>
        new(
            Guid.CreateVersion7(),
            courseVersionId,
            1,
            $"Kühlkette {Guid.CreateVersion7()}",
            24,
            [
                new SentChapter(Guid.CreateVersion7(), 3, "Film", "Video", videoSeconds, null, "chapters/film.mp4"),
                new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null),
                new SentChapter(Guid.CreateVersion7(), 2, "Handbuch", "Pdf", null, null, "chapters/handbuch.pdf"),
            ],
            new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));

    /// <summary>Sends the event under a new envelope id and waits until Learning's inbox has handled it.</summary>
    public static async Task DeliverAsync(AspireFixture aspire, PublishedEvent published, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await Broker.SendAsync(aspire, PublishedType, published, messageId, cancellationToken);
        await WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    /// <summary>
    /// The barrier: Learning's durable inbox marks the envelope handled once its consumer is done with it. A later
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
                throw new TimeoutException($"Learning's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Polls Learning's copy of the published versions until it holds the version, which only its consumer of Editorial's announcement writes.</summary>
    public static async Task WaitUntilHeldAsync(AspireFixture aspire, Guid courseVersionId, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (await CountAsync(aspire, "select count(*) from learning.course_versions where id = @id", courseVersionId, cancellationToken) == 0)
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(WaitTimeout, $"Learning never received course version {courseVersionId}.");
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
        var connectionString = await aspire.App.GetConnectionStringAsync("learning-database", cancellationToken)
            ?? throw new InvalidOperationException("Learning's database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    public sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    public sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
