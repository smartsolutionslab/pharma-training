using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Who may publish which event (#202, docs/adr/0011). A service's broker user may write only to its own events'
/// exchanges, but the message's type, which Wolverine dispatches by, is the publisher's to choose. So each message carries
/// the AMQP <c>user_id</c>, which RabbitMQ refuses unless it is the connection's user, and Compliance dead-letters, without
/// retrying, an event of a module other than the one whose user sent it: a course version Organisation forges on its own
/// <c>MembershipStartedV1</c> exchange, bound to Compliance's queue, is never stored. Each attempt is made with the
/// credentials Organisation's service holds.
/// </summary>
public class BrokerPublisherIntegrityTests(AspireFixture aspire)
{
    private const string Organisation = "organisation";

    private const string Editorial = "editorial";

    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string MembershipStarted = Contracts + "Organisation.MembershipStartedV1";

    private const string CourseVersionPublished = Contracts + "Editorial.CourseVersionPublishedV1";

    private const string Forged = "SmartSolutionsLab.PharmaTraining.Host.Messaging.ForgedPublisherException";

    // RabbitMQ's reply code for a message whose user_id is not the connection's user (PRECONDITION_FAILED).
    private const ushort PreconditionFailed = 406;

    // Long enough for a handled message; short of the minute Compliance's retries take before a dead letter, so a
    // dead letter within it was not retried.
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // How long a forwarded envelope is given to reach the consumer on the local queue; a real one takes a second or two.
    private static readonly TimeSpan ForwardWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_course_version_organisation_forges_on_its_membership_exchange_is_dead_lettered_at_once_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersion = CourseVersion();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        // As Organisation's Wolverine would send it, with its own user_id, which the broker accepts.
        await PublishAsync(organisation, MembershipStarted, CourseVersionPublished, courseVersion, messageId, BrokerUsers.UserOf(organisation), cancellationToken);

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.ShouldBe(Outcome.DeadLettered(Forged), $"Compliance's inbox holds the forged course version {messageId} as {outcome}.");
        (await CourseVersionsAsync(courseVersion.CourseVersionId, cancellationToken)).ShouldBe(0, "Compliance stored the course version Organisation forged.");
    }

    [Fact]
    public async Task An_event_without_a_user_id_is_dead_lettered_at_once_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        // Organisation's own event on its own exchange, but without the user_id that says who sent it.
        await PublishAsync(organisation, MembershipStarted, MembershipStarted, started, messageId, userId: null, cancellationToken);

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.ShouldBe(Outcome.DeadLettered(Forged), $"Compliance's inbox holds the membership {messageId} without a user_id as {outcome}.");
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(0, "Compliance stored a membership whose publisher no broker user vouches for.");
    }

    /// <summary>
    /// Wolverine handles a <c>scheduled-envelope</c> itself, outside the consumers' chains and so outside any middleware,
    /// by forwarding the envelope inside it to the destination that envelope names, a local queue among them. Only a
    /// module's integration event may come through the broker, so the wrapper is dead-lettered before it is handled.
    /// </summary>
    [Fact]
    public async Task A_course_version_organisation_wraps_in_a_scheduled_envelope_is_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersion = CourseVersion();
        var inner = new Envelope
        {
            Id = Guid.CreateVersion7(),
            MessageType = CourseVersionPublished,
            ContentType = "application/json",
            Data = JsonSerializer.SerializeToUtf8Bytes(courseVersion, JsonSerializerOptions.Web),
            Destination = new Uri("local://durable/"),
        };
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        await using (var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            var properties = Properties("scheduled-envelope", messageId, BrokerUsers.UserOf(organisation));
            properties.ContentType = "binary/envelope";
            await channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, properties, EnvelopeSerializer.Serialize(inner), cancellationToken);
        }

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.State.ShouldStartWith("dead-lettered", customMessage: $"Compliance's inbox holds the scheduled envelope {messageId} as {outcome}; Wolverine's own handler forwards what it carries.");
        await Task.Delay(ForwardWait, cancellationToken);
        (await CourseVersionsAsync(courseVersion.CourseVersionId, cancellationToken)).ShouldBe(0, "Compliance stored the course version Organisation forged inside a scheduled envelope.");
    }

    /// <summary>
    /// A header Wolverine reads sent as an AMQP void (null) makes its own mapping throw a <c>NullReferenceException</c>;
    /// refused like any message Wolverine cannot read, it is dead-lettered and never handled.
    /// </summary>
    [Fact]
    public async Task An_event_with_a_void_header_wolverine_reads_is_dead_lettered_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        await using (var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            var properties = Properties(MembershipStarted, messageId, BrokerUsers.UserOf(organisation));
            properties.Headers = new Dictionary<string, object?> { ["reply-uri"] = null };
            await channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(started, JsonSerializerOptions.Web), cancellationToken);
        }

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.State.ShouldStartWith("dead-lettered", customMessage: $"Compliance's inbox holds the membership {messageId} with a void reply-uri as {outcome}.");
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// A header Wolverine's own mapping cannot read (a <c>reply-uri</c> that is no URI) makes it throw before the envelope
    /// is received, and Wolverine 6.64 cannot dead-letter such an envelope, so it would be tried again without end. The
    /// message is refused like any other that is no plain integration event: dead-lettered, never handled.
    /// </summary>
    [Fact]
    public async Task An_event_whose_headers_wolverine_cannot_read_is_dead_lettered_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        await using (var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            var properties = Properties(MembershipStarted, messageId, BrokerUsers.UserOf(organisation));
            properties.Headers = new Dictionary<string, object?> { ["reply-uri"] = "not a uri" };
            await channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(started, JsonSerializerOptions.Web), cancellationToken);
        }

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.State.ShouldStartWith("dead-lettered", customMessage: $"Compliance's inbox holds the membership {messageId} with an unreadable reply-uri as {outcome}.");
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// A type that is a module's event name with a NUL after it (#230) named the module, so it was not refused; PostgreSQL
    /// then refused to store it in the inbox (22021), and Wolverine paused the listener and requeued the message without
    /// end, so the next event Compliance's queue held was never handled. Refused like an unknown type, it is dead-lettered
    /// and the listener goes on.
    /// </summary>
    [Fact]
    public async Task An_event_whose_type_has_a_nul_is_dead_lettered_and_the_next_event_is_still_handled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var poisoned = MembershipStartedEvent();
        var poisonedId = Guid.CreateVersion7();
        var next = MembershipStartedEvent();
        var nextId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        var user = BrokerUsers.UserOf(organisation);

        // Organisation's own event on its own exchange, with its own user_id, typed with a NUL after the event's name.
        await PublishAsync(organisation, MembershipStarted, MembershipStarted + "\0", poisoned, poisonedId, user, cancellationToken);
        await PublishAsync(organisation, MembershipStarted, MembershipStarted, next, nextId, user, cancellationToken);

        var nextOutcome = await OutcomeAsync(nextId, cancellationToken);
        var poisonedOutcome = await OutcomeAsync(poisonedId, cancellationToken);
        nextOutcome.ShouldBe(
            Outcome.Handled,
            $"Compliance's inbox holds the valid membership {nextId}, published after one typed '{MembershipStarted}\\0' ({poisonedId}, {poisonedOutcome}), as {nextOutcome}: the listener stopped consuming.");
        (await MembersAsync(next.MembershipId, cancellationToken)).ShouldBe(1, $"Compliance did not store the valid membership {next.MembershipId} published after the one typed with a NUL.");
        poisonedOutcome.State.ShouldStartWith("dead-lettered", customMessage: $"Compliance's inbox holds the membership {poisonedId} typed '{MembershipStarted}\\0' as {poisonedOutcome}, not dead-lettered.");
        (await MembersAsync(poisoned.MembershipId, cancellationToken)).ShouldBe(0, $"Compliance stored the membership {poisoned.MembershipId} whose type has a NUL.");
    }

    /// <summary>
    /// #338: besides the type, a publisher chooses the AMQP <c>app_id</c>, <c>correlation_id</c> and <c>reply_to</c>, and
    /// headers Wolverine reads into the envelope (<c>source</c>, <c>tenant-id</c>, <c>saga-id</c>) or keeps. A NUL in any of
    /// them must not reach a text column of the inbox or the dead letters, where PostgreSQL refuses it (22021) and the
    /// listener stops: the event is handled or dead-lettered, and the next one is handled.
    /// </summary>
    [Theory]
    [InlineData("app_id")]
    [InlineData("correlation_id")]
    [InlineData("reply_to")]
    [InlineData("header:source")]
    [InlineData("header:tenant-id")]
    [InlineData("header:saga-id")]
    [InlineData("header:x-note")]
    public async Task An_event_with_a_nul_in_a_field_its_publisher_chose_is_settled_and_the_next_event_is_still_handled(string field) =>
        await NulInPublisherFieldAsync(field, MembershipStarted);

    /// <summary>
    /// The same NUL on an event that is dead-lettered (its type is no module's event): the dead letter's row holds what
    /// Wolverine read from the message (<c>source</c> among it), so it is where a NUL would reach a text column.
    /// </summary>
    [Theory]
    [InlineData("app_id")]
    [InlineData("correlation_id")]
    [InlineData("reply_to")]
    [InlineData("header:source")]
    [InlineData("header:tenant-id")]
    [InlineData("header:saga-id")]
    [InlineData("header:x-note")]
    public async Task A_refused_event_with_a_nul_in_a_field_its_publisher_chose_is_dead_lettered_and_the_next_event_is_still_handled(string field) =>
        await NulInPublisherFieldAsync(field, Forged);

    /// <summary>
    /// #232: the dead letter's <c>source</c> column is text and takes the envelope's <c>Source</c>, read from the
    /// publisher's <c>source</c> header. With a NUL in it, PostgreSQL refused the insert (22021) and Wolverine completed
    /// the refused message: neither handled nor kept. The mapper escapes it, so the dead letter is there to be found.
    /// </summary>
    [Fact]
    public async Task A_refused_event_with_a_nul_in_its_source_is_found_in_the_dead_letters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var refused = MembershipStartedEvent();
        var refusedId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        var properties = Properties(Forged, refusedId, BrokerUsers.UserOf(organisation));
        properties.Headers = new Dictionary<string, object?> { ["source"] = System.Text.Encoding.UTF8.GetBytes("pharma\0source") };

        await PublishAsync(organisation, MembershipStarted, properties, refused, cancellationToken);

        var outcome = await OutcomeAsync(refusedId, cancellationToken);
        outcome.State.ShouldStartWith("dead-lettered", customMessage: $"The refused membership {refusedId} with a NUL in its source is not in wolverine_dead_letters, but {outcome}.");
        (await DeadLetterSourceAsync(refusedId, cancellationToken)).ShouldBe("pharma\\u0000source");
    }

    private async Task NulInPublisherFieldAsync(string field, string type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var poisoned = MembershipStartedEvent();
        var poisonedId = Guid.CreateVersion7();
        var next = MembershipStartedEvent();
        var nextId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        var user = BrokerUsers.UserOf(organisation);
        var properties = Properties(type, poisonedId, user);
        var value = $"pharma\0{field}";
        switch (field)
        {
            case "app_id":
                properties.AppId = value;
                break;
            case "correlation_id":
                properties.CorrelationId = value;
                break;
            case "reply_to":
                properties.ReplyTo = value;
                break;
            default:
                properties.Headers = new Dictionary<string, object?> { [field["header:".Length..]] = System.Text.Encoding.UTF8.GetBytes(value) };
                break;
        }

        await PublishAsync(organisation, MembershipStarted, properties, poisoned, cancellationToken);
        await PublishAsync(organisation, MembershipStarted, MembershipStarted, next, nextId, user, cancellationToken);

        var nextOutcome = await OutcomeAsync(nextId, cancellationToken);
        var poisonedOutcome = await OutcomeAsync(poisonedId, cancellationToken);
        nextOutcome.ShouldBe(
            Outcome.Handled,
            $"Compliance's inbox holds the valid membership {nextId}, published after one with a NUL in {field} ({poisonedId}, {poisonedOutcome}), as {nextOutcome}: the listener stopped consuming.");
        poisonedOutcome.ShouldNotBe(Outcome.Pending, $"The membership {poisonedId} with a NUL in {field} was neither handled nor dead-lettered.");
    }

    [Fact]
    public async Task An_event_its_own_modules_user_publishes_is_handled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        await PublishAsync(organisation, MembershipStarted, MembershipStarted, started, messageId, BrokerUsers.UserOf(organisation), cancellationToken);

        (await OutcomeAsync(messageId, cancellationToken)).ShouldBe(Outcome.Handled);
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// What the check relies on: the broker itself refuses a user_id that is not the connection's user, so Organisation
    /// cannot claim to be Editorial.
    /// </summary>
    [Fact]
    public async Task The_broker_refuses_a_user_id_other_than_the_connections_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        var editorial = BrokerUsers.UserOf(await BrokerUsers.ConnectionStringOfAsync(aspire, Editorial, cancellationToken));
        await using var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(CourseVersion(), JsonSerializerOptions.Web);
        var refusal = await BrokerUsers.RefusalOfAsync(
            connection,
            channel => channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, Properties(CourseVersionPublished, Guid.CreateVersion7(), editorial), body, cancellationToken).AsTask(),
            cancellationToken);

        refusal.ShouldNotBeNull($"The broker let Organisation's credentials publish with the user_id '{editorial}'.");
        refusal.ReplyCode.ShouldBe(PreconditionFailed, $"The publish failed with {refusal.ReplyCode} {refusal.ReplyText}, not PRECONDITION_FAILED.");
    }

    private static BasicProperties Properties(string type, Guid messageId, string? userId) => new()
    {
        Type = type,
        ContentType = "application/json",
        MessageId = messageId.ToString(),
        DeliveryMode = DeliveryModes.Persistent,
        UserId = userId,
    };

    private static PublishedEvent CourseVersion() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            $"Gefälschte Kühlkette {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static StartedEvent MembershipStartedEvent() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), $"Ada Lovelace {Guid.CreateVersion7()}", "Pta", "Employee", TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>Publishes to the exchange, typed as given, through a connection with the given credentials.</summary>
    private async Task PublishAsync<T>(string connectionString, string exchange, string type, T message, Guid messageId, string? userId, CancellationToken cancellationToken)
    {
        await ExchangeExistsAsync(exchange, cancellationToken);
        await using var connection = await BrokerUsers.ConnectAsync(connectionString, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicPublishAsync(exchange, string.Empty, mandatory: false, Properties(type, messageId, userId), JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>Publishes to the exchange with exactly these properties, through a connection with the given credentials.</summary>
    private async Task PublishAsync<T>(string connectionString, string exchange, BasicProperties properties, T message, CancellationToken cancellationToken)
    {
        await ExchangeExistsAsync(exchange, cancellationToken);
        await using var connection = await BrokerUsers.ConnectAsync(connectionString, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicPublishAsync(exchange, string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>The exchange, which Compliance's Wolverine declares at startup, exists, so nothing is published into nowhere.</summary>
    private async Task ExchangeExistsAsync(string exchange, CancellationToken cancellationToken)
    {
        await using var admin = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(admin, exchange, cancellationToken);
    }

    /// <summary>What Compliance's inbox made of the envelope: handled, or dead-lettered with the exception's type.</summary>
    private async Task<Outcome> OutcomeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using (var deadLetter = new NpgsqlCommand("select exception_type from wolverine.wolverine_dead_letters where id = @id", connection))
            {
                deadLetter.Parameters.AddWithValue("id", messageId);
                if (await deadLetter.ExecuteScalarAsync(cancellationToken) is string exception)
                {
                    return Outcome.DeadLettered(exception);
                }
            }

            await using (var incoming = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection))
            {
                incoming.Parameters.AddWithValue("id", messageId);
                if (await incoming.ExecuteScalarAsync(cancellationToken) is "Handled")
                {
                    return Outcome.Handled;
                }
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                return Outcome.Pending;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>The source column of the envelope's dead letter in Compliance's database.</summary>
    private async Task<string?> DeadLetterSourceAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select source from wolverine.wolverine_dead_letters where id = @id", connection);
        command.Parameters.AddWithValue("id", messageId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task<long> CourseVersionsAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        await CountAsync("select count(*) from compliance.course_versions where id = @id", courseVersionId, cancellationToken);

    private async Task<long> MembersAsync(Guid membershipId, CancellationToken cancellationToken) =>
        await CountAsync("select count(*) from compliance.members where id = @id", membershipId, cancellationToken);

    private async Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<NpgsqlConnection> OpenComplianceDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>What became of an envelope in Compliance's inbox.</summary>
    private sealed record Outcome(string State)
    {
        public static Outcome Handled { get; } = new("Handled");

        public static Outcome Pending { get; } = new("neither handled nor dead-lettered");

        public static Outcome DeadLettered(string exception) => new($"dead-lettered with {exception}");
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
