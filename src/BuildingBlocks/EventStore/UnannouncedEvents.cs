using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using Marten;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// Finds a module's stored events of one type that have no marker in its <c>announced_events</c> (#277,
/// docs/adr/0016): one anti-join of Marten's events against the markers, both in the module's schema of its database,
/// oldest first (Marten's sequence). Only the row is read, never the event's data, so the module loads each one on its own.
/// </summary>
public static class UnannouncedEvents
{
    /// <summary>
    /// The query a module's sweep runs, with the parameters <c>@from</c>, <c>@until</c> (timestamptz) and <c>@limit</c>.
    /// The type is written into the query, not passed, so the planner can match a partial index on it.
    /// </summary>
    public static string Query(string schema, string storedType)
    {
        Ensure.That(schema).IsNotNullOrWhiteSpace().Satisfies(IsIdentifier, "A schema is a lower-case SQL identifier.");
        Ensure.That(storedType).IsNotNullOrWhiteSpace().Satisfies(IsIdentifier, "Marten's stored type name is a lower-case SQL identifier.");

        return $"""
            select e.id, e.stream_id, e.version, e.timestamp
            from {schema}.mt_events e
            where e.type = '{storedType}' and e.timestamp >= @from and e.timestamp <= @until
              and not exists (select 1 from {schema}.announced_events a
                              where a.stream_id = e.stream_id and a.stream_version = e.version)
            order by e.seq_id
            limit @limit
            """;
    }

    /// <summary>Those stored from <paramref name="storedFrom"/> to <paramref name="storedUntil"/>, oldest first, at most <paramref name="limit"/>.</summary>
    public static async Task<IReadOnlyList<UnannouncedEvent>> FindAsync(
        IDocumentStore store,
        string schema,
        string storedType,
        DateTimeOffset storedFrom,
        DateTimeOffset storedUntil,
        int limit,
        CancellationToken cancellationToken)
    {
        Ensure.That(store).IsNotNull();

        await using var connection = store.Storage.Database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Query(schema, storedType), connection);
        command.Parameters.AddWithValue("from", storedFrom.ToUniversalTime());
        command.Parameters.AddWithValue("until", storedUntil.ToUniversalTime());
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<UnannouncedEvent> found = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new UnannouncedEvent(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return found;
    }

    private static bool IsIdentifier(string name) =>
        name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') && name[0] is not (>= '0' and <= '9');
}
