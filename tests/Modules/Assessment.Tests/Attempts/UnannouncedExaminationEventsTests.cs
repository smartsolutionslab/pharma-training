using System.Data.Common;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Npgsql;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// Loading a stored failure or lock for Assessment's sweeps (#307): the examination's stream is read up to the event's
/// version and replayed, so the event comes with its examination as the stream held it then. A database failure Marten
/// wraps once its command runs arrives as the DbException it wraps (#337), so the pass ends on the outage instead of
/// skipping the event as unreadable; a stream that no longer holds the event at its version is unreadable.
/// </summary>
public class UnannouncedExaminationEventsTests
{
    [Fact]
    public async Task A_stored_failure_is_loaded_with_its_examination_as_the_stream_held_it_then()
    {
        var fixture = new AttemptFixture();
        await fixture.OpenAsync();
        await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
        var history = fixture.Stream().ToList();
        var version = history.FindIndex(stored => stored is AttemptSubmitted) + 1;
        var found = new UnannouncedEvent(Guid.CreateVersion7(), fixture.Examination.Value, version, Now, 42);
        var finder = new UnannouncedTestFailures(StoreHolding(found, [.. history.Take(version)]));

        var loaded = await finder.LoadAsync(found, TestContext.Current.CancellationToken);

        loaded.Event.ShouldBeSameAs(history[version - 1]);
        loaded.Examination.Id.ShouldBe(fixture.Examination);
        loaded.Examination.Attempts.ShouldHaveSingleItem().Number.ShouldBe(AttemptNumber.First);
        ((IEventSourced)loaded.Examination).Version.ShouldBe(version);
    }

    [Fact]
    public async Task A_stream_that_no_longer_holds_the_event_at_its_version_cannot_be_read()
    {
        var fixture = new AttemptFixture();
        await fixture.OpenAsync();
        var found = new UnannouncedEvent(Guid.CreateVersion7(), fixture.Examination.Value, 1, Now, 42);
        var store = StoreHolding(new UnannouncedEvent(Guid.CreateVersion7(), found.StreamId, 1, Now, 41), [.. fixture.Stream()]);

        await Should.ThrowAsync<InvalidOperationException>(() => new UnannouncedExhaustions(store).LoadAsync(found, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_database_failure_Marten_wraps_while_a_lock_is_loaded_arrives_as_the_DbException_it_wraps()
    {
        var dropped = new NpgsqlException("Exception while reading from stream", new EndOfStreamException());
        var finder = new UnannouncedExhaustions(StoreThatFailsWith(new MartenCommandException(new NpgsqlCommand("select data from mt_events"), dropped)));

        var thrown = await Should.ThrowAsync<DbException>(() => finder.LoadAsync(Found(), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(dropped);
    }

    [Fact]
    public async Task A_database_failure_Marten_wraps_while_a_failure_is_loaded_arrives_as_the_DbException_it_wraps()
    {
        var dropped = new NpgsqlException("Exception while reading from stream", new EndOfStreamException());
        var finder = new UnannouncedTestFailures(StoreThatFailsWith(new MartenCommandException(new NpgsqlCommand("select data from mt_events"), dropped)));

        var thrown = await Should.ThrowAsync<DbException>(() => finder.LoadAsync(Found(), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(dropped);
    }

    /// <summary>A store whose stream of <paramref name="found"/> holds <paramref name="history"/>, the last of it stored as <paramref name="found"/>.</summary>
    private static IDocumentStore StoreHolding(UnannouncedEvent found, List<IDomainEvent> history)
    {
        IReadOnlyList<IEvent> stream = [.. history.Select((data, index) => Stored(data, index + 1L, index == history.Count - 1 ? found.EventId : Guid.CreateVersion7()))];
        var session = Substitute.For<IQuerySession>();
        session.Events.FetchStreamAsync(found.StreamId, found.StreamVersion, Arg.Any<DateTimeOffset?>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(stream);
        var store = Substitute.For<IDocumentStore>();
        store.QuerySession().Returns(session);
        return store;
    }

    private static IEvent Stored(IDomainEvent data, long version, Guid id)
    {
        var stored = Substitute.For<IEvent>();
        stored.Data.Returns(data);
        stored.Version.Returns(version);
        stored.Id.Returns(id);
        return stored;
    }

    private static IDocumentStore StoreThatFailsWith(Exception failure)
    {
        var session = Substitute.For<IQuerySession>();
        session.Events.FetchStreamAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTimeOffset?>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var store = Substitute.For<IDocumentStore>();
        store.QuerySession().Returns(session);
        return store;
    }

    private static UnannouncedEvent Found() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, TimeProvider.System.GetUtcNow(), 42);
}
