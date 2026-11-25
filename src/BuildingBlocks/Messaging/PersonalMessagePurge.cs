using System.Text.RegularExpressions;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The purge over the service's own database (#115): one statement per table, in one transaction, so a failure keeps
/// both tables as they were and the caller's retry purges them again. A body holds the id as Wolverine's JSON writes a
/// <see cref="Guid"/>: its D form, lowercase. The sweep of erased persons' messages (#439) matches every person of a
/// module's tombstones the same way, joined in one statement per table, under a lock that keeps a second sweep of the
/// same database from running the pass at once.
/// </summary>
public sealed partial class PersonalMessagePurge(NpgsqlDataSource dataSource) : IPersonalMessagePurge
{
    /// <summary>A module's schema as the sweep names it in SQL: a plain lowercase identifier, so it is never quoted input.</summary>
    public const string SchemaPattern = @"^[a-z][a-z0-9_]{0,62}\z";

    /// <summary>
    /// The name the sweep's advisory lock is keyed by, <c>hashtextextended(SweepLockKey, 0)</c>: a pass of
    /// <see cref="PurgeErasedAsync"/> takes it first and skips while another session holds it.
    /// </summary>
    public const string SweepLockKey = "wolverine.erased-person-sweep";

    // Wolverine's storage in the module's database, in the schema the MigrationRunner builds it in; the outbox,
    // wolverine_outgoing_envelopes, is never touched: what is in it is still to be sent. An incoming envelope is purged
    // only once it is handled, so a message still waiting is delivered.
    private const string DeadLetters =
        $"""
        DELETE FROM {ModuleMessageStorage.Schema}.wolverine_dead_letters
         WHERE message_type = ANY(@types) AND position(convert_to(@id, 'UTF8') in body) > 0
        """;

    private const string HandledEnvelopes =
        $"""
        DELETE FROM {ModuleMessageStorage.Schema}.wolverine_incoming_envelopes
         WHERE status = 'Handled' AND message_type = ANY(@types) AND position(convert_to(@id, 'UTF8') in body) > 0
        """;

    // One sweep per database at a time: a replica that finds the lock taken skips its pass. Advisory locks are per
    // database, and every service has a database of its own.
    private const string SweepLock = $"SELECT pg_try_advisory_xact_lock(hashtextextended('{SweepLockKey}', 0))";

    private readonly NpgsqlDataSource dataSource = dataSource;

    public async Task PurgeAsync(Guid personId, IReadOnlyCollection<Type> types, CancellationToken cancellationToken)
    {
        Ensure.That(personId, nameof(personId)).IsNotEmpty();
        Ensure.That(types).IsNotNull();

        var names = Names(types);
        if (names.Length == 0)
        {
            return;
        }

        var id = personId.ToString("D");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var statement in new[] { DeadLetters, HandledEnvelopes })
        {
            await using var command = new NpgsqlCommand(statement, connection, transaction);
            command.Parameters.AddWithValue("types", names);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> PurgeErasedAsync(string erasedPersonsSchema, IReadOnlyCollection<Type> types, CancellationToken cancellationToken)
    {
        Ensure.That(erasedPersonsSchema, nameof(erasedPersonsSchema))
            .IsNotNullOrWhiteSpace()
            .Matches(PlainSchema(), "must be a plain lowercase identifier.");
        Ensure.That(types).IsNotNull();

        var names = Names(types);
        if (names.Length == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var locking = new NpgsqlCommand(SweepLock, connection, transaction))
        {
            if (await locking.ExecuteScalarAsync(cancellationToken) is not true)
            {
                await transaction.RollbackAsync(cancellationToken);
                return 0;
            }
        }

        var removed = 0;
        foreach (var statement in new[] { ErasedDeadLetters(erasedPersonsSchema), ErasedHandledEnvelopes(erasedPersonsSchema) })
        {
            await using var command = new NpgsqlCommand(statement, connection, transaction);
            command.Parameters.AddWithValue("types", names);
            removed += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    private static string[] Names(IReadOnlyCollection<Type> types) => [.. types.Select(type => type.FullName ?? type.Name)];

    // A Guid in its lowercase D form, as Wolverine's JSON writes one. The sweep takes every such id out of a body and looks
    // it up in the module's tombstones by their key, so a pass costs the bodies it reads, whatever the number of erased
    // persons (#462); matching each tombstone's id against each body cost both multiplied. encode(..., 'escape') leaves an
    // id's ASCII as it is and reads any body, even one that is not UTF-8. It matches what position() matched: the id's D
    // form anywhere in the body.
    private const string IdPattern = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

    // The schema is checked against SchemaPattern before it is named here, so it is a plain identifier, never input.
    private static string ErasedDeadLetters(string schema) =>
        $"""
        DELETE FROM {ModuleMessageStorage.Schema}.wolverine_dead_letters AS letter
         WHERE letter.message_type = ANY(@types)
           AND EXISTS (SELECT 1 FROM regexp_matches(encode(letter.body, 'escape'), '{IdPattern}', 'g') AS found(id)
                          JOIN "{schema}".erased_persons AS erased ON erased.person_id = found.id[1]::uuid)
        """;

    private static string ErasedHandledEnvelopes(string schema) =>
        $"""
        DELETE FROM {ModuleMessageStorage.Schema}.wolverine_incoming_envelopes AS envelope
         WHERE envelope.status = 'Handled' AND envelope.message_type = ANY(@types)
           AND EXISTS (SELECT 1 FROM regexp_matches(encode(envelope.body, 'escape'), '{IdPattern}', 'g') AS found(id)
                          JOIN "{schema}".erased_persons AS erased ON erased.person_id = found.id[1]::uuid)
        """;

    [GeneratedRegex(SchemaPattern, RegexOptions.CultureInvariant)]
    internal static partial Regex PlainSchema();
}
