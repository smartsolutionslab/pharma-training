using Aspire.Hosting.Testing;
using JasperFx;
using Marten;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's side of the tests: Editorial's <c>CourseVersionPublishedV1</c> and <c>CourseVersionRetiredV1</c> and Organisation's membership events sent
/// as their publishers' Wolverine sends them, the barrier that Learning's inbox handled them, and Learning's own tables
/// read directly.
/// <para>
/// Learning lets only active members of a pharmacy start or work on a session (#194), so a test's learner becomes one
/// first through <see cref="MemberAsync(AspireFixture, Guid, CancellationToken)"/>: a membership of a fresh pharmacy,
/// delivered straight to the broker. No test may make <c>visitor</c> (the person who is never a member) or
/// <c>former-employee</c> (whose every membership is ended, in <c>LearningMembershipTests</c> alone) a member anywhere
/// else, or those tests stop meaning anything.
/// </para>
/// </summary>
internal static class LearningDatabase
{
    public const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";

    public const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    public const string ChangedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";

    public const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";

    public const string RetiredType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionRetiredV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The length of a video a test reports on at once: video progress may not outpace playback (#43, ADR 0024), and
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
    public static Task DeliverAsync(AspireFixture aspire, PublishedEvent published, CancellationToken cancellationToken) =>
        DeliverAsync(aspire, PublishedType, published, cancellationToken);

    /// <summary>Sends any event under a new envelope id and waits until Learning's inbox has handled it.</summary>
    public static async Task DeliverAsync<T>(AspireFixture aspire, string type, T message, CancellationToken cancellationToken)
    {
        // A tombstoned person's membership message is dropped and its handled envelope swept (#439), maybe before a
        // poll sees it Handled, so the sweep waits until this barrier is past.
        var messageId = Guid.CreateVersion7();
        await using var pause = await ErasedPersonSweepPause.StartAsync(aspire, "learning", type, cancellationToken);
        await Broker.SendAsync(aspire, type, message, messageId, cancellationToken);
        await WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    /// <summary>Makes the person a member of a fresh pharmacy in Learning's copy, and answers the membership's id.</summary>
    public static Task<Guid> MemberAsync(AspireFixture aspire, Guid person, CancellationToken cancellationToken) =>
        MemberAsync(aspire, person, Guid.CreateVersion7(), cancellationToken);

    /// <summary>Makes the person a member of the pharmacy in Learning's copy, and answers the membership's id.</summary>
    public static async Task<Guid> MemberAsync(AspireFixture aspire, Guid person, Guid pharmacy, CancellationToken cancellationToken)
    {
        var started = Started(Guid.CreateVersion7(), person, pharmacy);
        await DeliverAsync(aspire, StartedType, started, cancellationToken);
        return started.MembershipId;
    }

    /// <summary>Ends the membership in Learning's copy.</summary>
    public static Task EndAsync(AspireFixture aspire, Guid membership, Guid person, Guid pharmacy, CancellationToken cancellationToken) =>
        DeliverAsync(aspire, EndedType, Ended(membership, person, pharmacy), cancellationToken);

    /// <summary>A membership's start as Organisation publishes it.</summary>
    public static StartedEvent Started(Guid membership, Guid person, Guid pharmacy) =>
        new(membership, person, pharmacy, $"Learner {person}", "Pta", "Employee", TimeProvider.System.GetUtcNow(), new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));

    /// <summary>A change of a membership's job function or role as Organisation publishes it; version 2 is the first change.</summary>
    public static ChangedEvent Changed(Guid membership, Guid person, Guid pharmacy, int version) =>
        new(membership, person, pharmacy, $"Learner {person}", "Pta", "Manager", version, new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));

    /// <summary>A membership's ending as Organisation publishes it.</summary>
    public static EndedEvent Ended(Guid membership, Guid person, Guid pharmacy) =>
        new(membership, person, pharmacy, TimeProvider.System.GetUtcNow(), new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));

    /// <summary>The retirement of the published version, under metadata of its own each time it is built.</summary>
    public static RetiredEvent Retired(PublishedEvent published)
    {
        Ensure.That(published).IsNotNull();

        return new(published.CourseId, published.CourseVersionId, new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));
    }

    /// <summary>Sends the retirement under a new envelope id and waits until Learning's inbox has handled it.</summary>
    public static async Task DeliverAsync(AspireFixture aspire, RetiredEvent retired, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await Broker.SendAsync(aspire, RetiredType, retired, messageId, cancellationToken);
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

    /// <summary>Polls Learning's retirements until it holds the version's, which only its consumer of Editorial's retirement writes.</summary>
    public static async Task WaitUntilRetiredAsync(AspireFixture aspire, Guid courseVersionId, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (await CountAsync(aspire, "select count(*) from learning.course_version_retirements where course_version_id = @id", courseVersionId, cancellationToken) == 0)
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(WaitTimeout, $"Learning never heard that course version {courseVersionId} was retired.");
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Appends a <c>ContentCompleted</c> to the session's stream in Learning's event store, with no marker and no
    /// announcement, as a crash between the report's append and its outbox commit leaves it (#277); answers the stream
    /// version Marten stored it at. The session's view is not projected, as the report's own append would project it.
    /// </summary>
    public static async Task<long> PlantCompletionAsync(AspireFixture aspire, PlantedContentCompleted completed, CancellationToken cancellationToken)
    {
        Ensure.That(completed).IsNotNull();

        var connectionString = await aspire.App.GetConnectionStringAsync("learning-database", cancellationToken)
            ?? throw new InvalidOperationException("Learning's database has no connection string.");
        await using var store = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "learning";
            options.Events.DatabaseSchemaName = "learning";
            options.AutoCreateSchemaObjects = AutoCreate.None;
            options.UseSystemTextJsonForSerialization();
            options.Events.MapEventType<PlantedContentCompleted>("content_completed");
        });
        await using var session = store.LightweightSession();
        var planted = session.Events.Append(completed.Session, completed).Events.ShouldHaveSingleItem();
        await session.SaveChangesAsync(cancellationToken);

        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select version from learning.mt_events where id = @id", connection);
        command.Parameters.AddWithValue("id", planted.Id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException($"Learning's event store holds no event {planted.Id}."));
    }

    /// <summary>The stream version of the stream's only event of the stored type (<c>content_completed</c>).</summary>
    public static async Task<long> StoredVersionAsync(AspireFixture aspire, Guid stream, string type, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select version from learning.mt_events where stream_id = @stream and type = @type", connection);
        command.Parameters.AddWithValue("stream", stream);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<long> versions = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions.ShouldHaveSingleItem($"stream {stream} should hold one {type}");
    }

    /// <summary>How many markers Learning holds for the stored event at the stream's version (#277): one once it was announced.</summary>
    public static async Task<long> CountAnnouncedAsync(AspireFixture aspire, Guid stream, long version, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select count(*) from learning.announced_events where stream_id = @stream and stream_version = @version", connection);
        command.Parameters.AddWithValue("stream", stream);
        command.Parameters.AddWithValue("version", version);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>How many sessions the learner has on the version, as Learning's session view holds them.</summary>
    public static async Task<long> CountSessionsAsync(AspireFixture aspire, Guid learner, Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select count(*) from learning.mt_doc_learningsessionview where data->>'LearnerId' = @learner and data->>'CourseVersionId' = @version",
            connection);
        command.Parameters.AddWithValue("learner", learner.ToString());
        command.Parameters.AddWithValue("version", courseVersionId.ToString());
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
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

    public sealed record RetiredEvent(Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    public sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    public sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    public sealed record ChangedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, int Version, EventMetadata Metadata);

    public sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);
}
