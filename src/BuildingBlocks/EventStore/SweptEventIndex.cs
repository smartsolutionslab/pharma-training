namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;

/// <summary>
/// A partial index on the stored time of one type of a module's events, <c>mt_events(timestamp) where type = '…'</c>, so
/// its announcement sweep (#277, docs/adr/0016) reads only the terminal events of its window, never the whole event
/// table, which grows with every event the module stores. The MigrationRunner makes it right after Marten has made the
/// event tables (<see cref="Microsoft.Extensions.DependencyInjection.EventStoreRegistration.ApplyEventStoreSchemasAsync"/>),
/// and the module's store tells Marten to leave it alone (<see cref="ModuleEventStore"/>).
/// </summary>
public sealed class SweptEventIndex
{
    public SweptEventIndex(string schema, string storedType)
    {
        // Query refuses a schema or type that is not a plain SQL identifier; the index is written from the same two.
        _ = UnannouncedEvents.Query(schema, storedType);

        Name = NameOf(storedType);
        CreateStatement = $"create index if not exists {Name} on {schema}.mt_events (timestamp) where type = '{storedType}'";
    }

    public string Name { get; }

    /// <summary>Idempotent: it makes the index where it is missing and leaves it as it is otherwise.</summary>
    public string CreateStatement { get; }

    /// <summary>The index's name for the events of <paramref name="storedType"/>.</summary>
    public static string NameOf(string storedType) => $"ix_mt_events_{storedType}_timestamp";
}
