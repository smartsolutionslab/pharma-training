using System.Data.Common;
using Marten;
using Marten.Exceptions;
using Npgsql;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.EventStore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// Loading a stored test pass for Assessment's announcement sweep (#277, #337): a database failure once Marten's command runs (a
/// dropped connection, a command timeout) reaches Marten's exception transformer, which wraps the NpgsqlException in a
/// MartenCommandException, no DbException. The finder hands the sweep the DbException it wraps, so the pass ends on the
/// outage instead of skipping the event as unreadable, and the Application never sees Marten's types.
/// </summary>
public class UnannouncedTestPassesTests
{
    [Fact]
    public async Task A_database_failure_Marten_wraps_while_a_test_pass_is_loaded_arrives_as_the_DbException_it_wraps()
    {
        var dropped = new NpgsqlException("Exception while reading from stream", new EndOfStreamException());
        var finder = new UnannouncedTestPasses(StoreThatFailsWith(new MartenCommandException(new NpgsqlCommand("select data from mt_events"), dropped)));

        var thrown = await Should.ThrowAsync<DbException>(() => finder.LoadAsync(Found(), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(dropped);
    }

    [Fact]
    public async Task A_failure_Marten_wraps_that_is_no_database_failure_is_left_as_Marten_threw_it()
    {
        var wrapped = new MartenCommandException(new NpgsqlCommand("select data from mt_events"), new InvalidOperationException("not a database failure"));
        var finder = new UnannouncedTestPasses(StoreThatFailsWith(wrapped));

        var thrown = await Should.ThrowAsync<MartenCommandException>(() => finder.LoadAsync(Found(), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(wrapped);
    }

    private static IDocumentStore StoreThatFailsWith(Exception failure)
    {
        var session = Substitute.For<IQuerySession>();
        session.Events.LoadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var store = Substitute.For<IDocumentStore>();
        store.QuerySession().Returns(session);
        return store;
    }

    private static UnannouncedEvent Found() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, TimeProvider.System.GetUtcNow(), 42);
}
