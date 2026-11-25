using System.Data;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore.Tests;

/// <summary>
/// <see cref="MartenAggregateStore.TrySaveAsync"/>, without a database: Marten's session ends the save as Marten 9.59
/// ends it, a 40001 in a <see cref="ConcurrentUpdateException"/>, a 40P01 (after Marten's own retries) in a
/// <see cref="MartenCommandException"/>, and another append in the expected-version check's exception. Another append on the stream since it was loaded is a race lost
/// to a version that moved on (the module's changed code, load again); a deadlock or serialization failure, the events
/// and the inline projections being written in one transaction, is a race lost to a deadlock (concurrent-save, try
/// again). Anything else still throws.
/// </summary>
public class StreamLostRaceTests
{
    private const string Deadlock = "a deadlock";

    private const string VersionChange = "a version that moved on";

    [Fact]
    public async Task An_append_after_another_landed_on_the_stream_is_lost_to_a_version_that_moved_on()
    {
        var store = StoreFailingWith(new EventStreamUnexpectedMaxEventIdException("The stream moved on."));

        ShouldHaveLostTo(VersionChange, await store.TrySaveAsync(Loaded(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_save_postgresql_ended_as_a_deadlock_is_lost_to_a_deadlock(string sqlState)
    {
        var store = StoreFailingWith(AsMartenThrows(Ended(sqlState)));

        ShouldHaveLostTo(Deadlock, await store.TrySaveAsync(Loaded(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_deadlock_beside_another_append_is_lost_to_the_deadlock()
    {
        var store = StoreFailingWith(new AggregateException(new EventStreamUnexpectedMaxEventIdException("The stream moved on."), AsMartenThrows(Ended(PostgresErrorCodes.DeadlockDetected))));

        ShouldHaveLostTo(Deadlock, await store.TrySaveAsync(Loaded(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A deadlock is logged as a warning naming its SqlState, with the PostgresException attached, as the EF modules log
    /// it, so one that keeps happening shows in the telemetry; a stream that moved on is ordinary and logs nothing.
    /// </summary>
    [Fact]
    public async Task A_deadlock_is_logged_as_a_warning_naming_its_sql_state_and_a_stream_that_moved_on_is_not()
    {
        var ended = Ended(PostgresErrorCodes.DeadlockDetected);

        await StoreFailingWith(AsMartenThrows(ended)).TrySaveAsync(Loaded(), TestContext.Current.CancellationToken);
        await StoreFailingWith(new EventStreamUnexpectedMaxEventIdException("The stream moved on.")).TrySaveAsync(Loaded(), TestContext.Current.CancellationToken);

        var record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain("40P01");
        record.Exception.ShouldBeSameAs(ended);
    }

    [Fact]
    public async Task Any_other_database_error_still_throws()
    {
        var store = StoreFailingWith(Ended(PostgresErrorCodes.UniqueViolation));

        var thrown = await Should.ThrowAsync<PostgresException>(() => store.TrySaveAsync(Loaded(), TestContext.Current.CancellationToken));
        thrown.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    /// <summary>Fails unless the save lost a race, and lost it to <paramref name="expected"/>.</summary>
    private static void ShouldHaveLostTo(string expected, Result<Unit, LostRace> saved)
    {
        saved.IsFailure.ShouldBeTrue("the save answered as stored");
        var lostTo = saved.Error switch
        {
            SaveDeadlocked => Deadlock,
            VersionMovedOn => VersionChange,
        };
        lostTo.ShouldBe(expected);
    }

    /// <summary>The exception Marten 9.59 throws around a PostgreSQL failure that ended a save: 40001 and 40P01 apart.</summary>
    private static Exception AsMartenThrows(PostgresException ended) => ended.SqlState == PostgresErrorCodes.SerializationFailure
        ? new ConcurrentUpdateException(ended)
        : new MartenCommandException(new NpgsqlCommand("insert into mt_events"), ended);

    private static PostgresException Ended(string sqlState) =>
        new($"the transaction was ended ({sqlState})", "ERROR", "ERROR", sqlState);

    /// <summary>An aggregate loaded at version 1 with one event pending, so the save appends to its stream.</summary>
    private static IEventSourced Loaded()
    {
        var aggregate = Substitute.For<IEventSourced>();
        var id = Substitute.For<IAggregateId>();
        id.Value.Returns(Guid.CreateVersion7());
        aggregate.Id.Returns(id);
        aggregate.Version.Returns(1L);
        aggregate.PendingEvents.Returns([Substitute.For<IDomainEvent>()]);
        return aggregate;
    }

    private readonly FakeLogger<TestAggregateStore> logger = new();

    private TestAggregateStore StoreFailingWith(Exception failure)
    {
        var session = Substitute.For<IDocumentSession>();
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var documents = Substitute.For<IDocumentStore>();
        documents.LightweightSession(Arg.Any<IsolationLevel>()).Returns(session);
        return new TestAggregateStore(documents, logger);
    }
}
