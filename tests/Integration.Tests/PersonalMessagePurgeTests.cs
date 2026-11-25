using System.Text;
using Aspire.Hosting.Testing;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The purge of a person's messages (#115, spec 4.6) against Wolverine's real storage in Compliance's database: it
/// deletes the dead letters, and the handled envelopes of the durable inbox, of the given message types whose body holds
/// the person's id in its D form, and nothing else: not another type, not another person, not an envelope still waiting
/// in the inbox, and never the outbox. The rows are planted by hand: a handled envelope and a dead letter are what
/// Wolverine leaves, a scheduled one is not due for a day, and the outgoing one belongs to no node of this run, so none
/// is delivered while the test looks.
/// </summary>
public class PersonalMessagePurgeTests(AspireFixture aspire)
{
    private const string Started = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string Changed = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    private const string Ended = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";

    private static readonly IReadOnlyCollection<Type> MembershipNames = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    [Fact]
    public async Task The_dead_letters_and_handled_envelopes_of_the_types_that_hold_the_person_are_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        var deadStart = await DeadLetterAsync(connection, Started, person, cancellationToken);
        var deadChange = await DeadLetterAsync(connection, Changed, person, cancellationToken);
        var handledStart = await IncomingAsync(connection, Started, person, "Handled", cancellationToken);
        var handledChange = await IncomingAsync(connection, Changed, person, "Handled", cancellationToken);

        await PurgeAsync(person, cancellationToken);

        (await CountAsync(connection, "wolverine_dead_letters", [deadStart, deadChange], cancellationToken)).ShouldBe(0);
        (await CountAsync(connection, "wolverine_incoming_envelopes", [handledStart, handledChange], cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Other_types_other_persons_waiting_envelopes_and_the_outbox_are_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
        var deadEnded = await DeadLetterAsync(connection, Ended, person, cancellationToken);
        var deadOfAnother = await DeadLetterAsync(connection, Started, someoneElse, cancellationToken);
        var handledEnded = await IncomingAsync(connection, Ended, person, "Handled", cancellationToken);
        var handledOfAnother = await IncomingAsync(connection, Changed, someoneElse, "Handled", cancellationToken);
        var waiting = await IncomingAsync(connection, Started, person, "Scheduled", cancellationToken);
        var outgoing = await OutgoingAsync(connection, Started, person, cancellationToken);

        await PurgeAsync(person, cancellationToken);

        (await CountAsync(connection, "wolverine_dead_letters", [deadEnded, deadOfAnother], cancellationToken)).ShouldBe(2);
        (await CountAsync(connection, "wolverine_incoming_envelopes", [handledEnded, handledOfAnother, waiting], cancellationToken)).ShouldBe(3);
        (await CountAsync(connection, "wolverine_outgoing_envelopes", [outgoing], cancellationToken)).ShouldBe(1, "the outbox is never touched");

        await DeleteAsync(connection, "wolverine_incoming_envelopes", waiting, cancellationToken);
        await DeleteAsync(connection, "wolverine_outgoing_envelopes", outgoing, cancellationToken);
    }

    private static byte[] Body(Guid person) =>
        Encoding.UTF8.GetBytes($$"""{"membershipId":"{{Guid.CreateVersion7()}}","personId":"{{person}}","pharmacyId":"{{Guid.CreateVersion7()}}","displayName":"Purge Probe {{person:N}}"}""");

    private static async Task<Guid> DeadLetterAsync(NpgsqlConnection connection, string type, Guid person, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_dead_letters (id, execution_time, body, message_type, received_at, source, exception_type, exception_message, sent_at, replayable)
            VALUES (@id, NULL, @body, @type, 'rabbitmq://queue/compliance', 'rabbitmq://exchange/' || @type, 'System.Exception', 'planted by PersonalMessagePurgeTests', now(), false)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task<Guid> IncomingAsync(NpgsqlConnection connection, string type, Guid person, string status, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_incoming_envelopes (id, status, owner_id, execution_time, attempts, body, message_type, received_at, keep_until)
            VALUES (@id, @status, 0, now() + interval '1 day', 0, @body, @type, 'rabbitmq://queue/compliance', now() + interval '1 day')
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task<Guid> OutgoingAsync(NpgsqlConnection connection, string type, Guid person, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO wolverine.wolverine_outgoing_envelopes (id, owner_id, destination, deliver_by, body, attempts, message_type)
            VALUES (@id, 2147483000, 'rabbitmq://exchange/purge-probe', now() + interval '1 day', @body, 0, @type)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("body", Body(person));
        command.Parameters.AddWithValue("type", type);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table, Guid[] ids, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM wolverine.{table} WHERE id = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", ids);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task DeleteAsync(NpgsqlConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"DELETE FROM wolverine.{table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task PurgeAsync(Guid person, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new PersonalMessagePurge(dataSource).PurgeAsync(person, MembershipNames, cancellationToken);
    }
}
