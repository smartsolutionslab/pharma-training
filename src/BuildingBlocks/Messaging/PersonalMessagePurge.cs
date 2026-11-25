using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The purge over the service's own database (#115): one statement per table, in one transaction, so a failure keeps
/// both tables as they were and the caller's retry purges them again. A body holds the id as Wolverine's JSON writes a
/// <see cref="Guid"/>: its D form, lowercase.
/// </summary>
public sealed class PersonalMessagePurge(NpgsqlDataSource dataSource) : IPersonalMessagePurge
{
    // Wolverine's storage in the module's database (ModuleMessageStorage.Schema); the outbox, wolverine_outgoing_envelopes,
    // is never touched: what is in it is still to be sent. An incoming envelope is purged only once it is handled, so a
    // message still waiting is delivered.
    private const string DeadLetters =
        """
        DELETE FROM wolverine.wolverine_dead_letters
         WHERE message_type = ANY(@types) AND position(convert_to(@id, 'UTF8') in body) > 0
        """;

    private const string HandledEnvelopes =
        """
        DELETE FROM wolverine.wolverine_incoming_envelopes
         WHERE status = 'Handled' AND message_type = ANY(@types) AND position(convert_to(@id, 'UTF8') in body) > 0
        """;

    private readonly NpgsqlDataSource dataSource = dataSource;

    public async Task PurgeAsync(Guid personId, IReadOnlyCollection<Type> types, CancellationToken cancellationToken)
    {
        Ensure.That(personId, nameof(personId)).IsNotEmpty();
        Ensure.That(types).IsNotNull();

        string[] names = [.. types.Select(type => type.FullName ?? type.Name)];
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
}
