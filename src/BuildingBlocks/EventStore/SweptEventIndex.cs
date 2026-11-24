namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// A partial index on the stored time of one type of a module's events, <c>mt_events(timestamp) where type = '…'</c>, so
/// its announcement sweep (#277, docs/adr/0016) reads only the terminal events of its window, never the whole event
/// table, which grows with every event the module stores. The MigrationRunner makes it right after Marten has made the
/// event tables (<see cref="Microsoft.Extensions.DependencyInjection.EventStoreRegistration.ApplyEventStoreSchemasAsync"/>),
/// and the module's store tells Marten to leave it alone (<see cref="ModuleEventStore"/>). It is built concurrently
/// (#337): a plain build holds a SHARE lock on <c>mt_events</c> for as long as it reads the table, and appends from the
/// replicas still running during a rolling deploy would wait for it. A concurrent build that fails leaves an invalid index
/// of its name behind, which <c>if not exists</c> would keep for good, so a run first drops an invalid one. The lock waits
/// of both are bounded by the runner's setting <see cref="IndexLockTimeout"/> (#351, #359).
/// </summary>
public sealed class SweptEventIndex
{
    public SweptEventIndex(string schema, string storedType)
    {
        // Query refuses a schema or type that is not a plain SQL identifier; the index is written from the same two.
        _ = UnannouncedEvents.Query(schema, storedType);

        Schema = schema;
        Name = NameOf(storedType);
        CreateStatement = $"create index concurrently if not exists {Name} on {schema}.mt_events (timestamp) where type = '{storedType}'";
        IsValidQuery = $"""
            select exists (select 1
                             from pg_catalog.pg_index i
                             join pg_catalog.pg_class c on c.oid = i.indexrelid
                             join pg_catalog.pg_namespace n on n.oid = c.relnamespace
                            where n.nspname = '{schema}' and c.relname = '{Name}' and i.indisvalid)
            """;
        IsInvalidQuery = $"""
            select exists (select 1
                             from pg_catalog.pg_index i
                             join pg_catalog.pg_class c on c.oid = i.indexrelid
                             join pg_catalog.pg_namespace n on n.oid = c.relnamespace
                            where n.nspname = '{schema}' and c.relname = '{Name}' and not i.indisvalid)
            """;
        DropStatement = $"drop index concurrently if exists {schema}.{Name}";
    }

    /// <summary>The schema of the module's event store, which holds <c>mt_events</c>.</summary>
    public string Schema { get; }

    public string Name { get; }

    /// <summary>
    /// Idempotent: it makes the index where it is missing and leaves it as it is otherwise, an invalid one included. It
    /// runs outside a transaction, and waits for the transactions that write to <c>mt_events</c> without stopping them.
    /// </summary>
    public string CreateStatement { get; }

    /// <summary>Answers whether an index of this name exists and is valid: a run then neither drops nor builds it.</summary>
    public string IsValidQuery { get; }

    /// <summary>Answers whether an index of this name exists and is invalid, as a concurrent build that failed leaves it.</summary>
    public string IsInvalidQuery { get; }

    /// <summary>Drops the index without stopping the writers of <c>mt_events</c>; a run sends it only for an invalid one.</summary>
    public string DropStatement { get; }

    /// <summary>The index's name for the events of <paramref name="storedType"/>.</summary>
    public static string NameOf(string storedType) => $"ix_mt_events_{storedType}_timestamp";
}
