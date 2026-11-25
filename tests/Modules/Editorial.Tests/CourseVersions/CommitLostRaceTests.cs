using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;
using Wolverine;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// <c>ModuleUnitOfWork.TryCommitAsync</c>, through Editorial's unit of work, without a database. When PostgreSQL ends a
/// save as a deadlock (40P01) or a serialization failure (40001), even at the COMMIT that SaveChangesAsync issues for
/// its own transaction (where a deferred key check closes the cycle), EFCore.PG reports it as a
/// <see cref="DbUpdateConcurrencyException"/> around the <see cref="PostgresException"/>; observed against PostgreSQL
/// for EFCore.PG 11.0.0-rc.3. That is a race lost to a deadlock (<c>SaveDeadlocked</c>, which a module answers as
/// <c>&lt;module&gt;.concurrent-save</c>), as is a 40P01 or 40001 raised by one of the save's statements, which EF Core wraps in a
/// plain <see cref="DbUpdateException"/>. A row version that moved on is a race lost to it (<c>VersionMovedOn</c>, the
/// module's changed code). A database error with any other SqlState still throws.
/// <para>
/// These tests build the <see cref="DbUpdateConcurrencyException"/> by hand, so the 40P01 or 40001 inside it plays no
/// part in them: only the integration test
/// <c>A_question_added_to_a_chapter_whose_removal_deadlocks_with_it_at_commit_is_told_to_try_again_never_a_server_error</c>
/// (<c>EditorialQuestionRacesTests</c>) pins that the provider wraps a commit-time deadlock that way.
/// </para>
/// <para>
/// When a handler publishes, Wolverine's EF Core envelope transaction commits through
/// <c>Database.CommitTransactionAsync</c> instead, and the same failure arrives as a bare <see cref="PostgresException"/>
/// (or inside another exception), with no <see cref="DbUpdateConcurrencyException"/> around it. That is a lost race
/// to a deadlock too; a <see cref="PostgresException"/> with any other SqlState still throws.
/// </para>
/// </summary>
public class CommitLostRaceTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_save_postgresql_ended_as_a_deadlock_at_its_commit_is_lost_to_a_deadlock(string sqlState)
    {
        await using var services = OutboxFailingWith(new DbUpdateConcurrencyException("An error occurred while saving the entity changes. See the inner exception for details.", Ended(sqlState)));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A save whose wait for a lock ran past its transaction's <c>lock_timeout</c> (55P03, which Organisation sets for
    /// the rows its invitation mails count on, #375) is lost like a deadlock, however it is wrapped: nothing was saved,
    /// and the same request may succeed when tried again.
    /// </summary>
    [Theory]
    [InlineData(Bare)]
    [InlineData(InsideAnotherException)]
    [InlineData(AtAStatement)]
    [InlineData(AtTheCommitEfCoreOwns)]
    [InlineData(BehindAnotherFailureInAnAggregate)]
    public async Task A_save_postgresql_ended_because_a_lock_wait_ran_past_the_lock_timeout_is_lost_to_a_deadlock(string wrapping)
    {
        await using var services = OutboxFailingWith(Wrapped(Ended(PostgresErrorCodes.LockNotAvailable), wrapping));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_save_that_moved_a_row_version_is_lost_to_a_version_that_moved_on()
    {
        await using var services = OutboxFailingWith(new DbUpdateConcurrencyException("The row version moved on."));

        ShouldHaveLostTo(VersionChange, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Any_other_database_error_still_throws()
    {
        await using var services = OutboxFailingWith(new DbUpdateException("An error occurred while saving the entity changes.", Ended(PostgresErrorCodes.UniqueViolation)));

        await Should.ThrowAsync<DbUpdateException>(() => new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_commit_postgresql_ended_as_a_deadlock_outside_ef_core_is_lost_to_a_deadlock(string sqlState)
    {
        await using var services = OutboxFailingWith(Ended(sqlState));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_commit_postgresql_ended_as_a_deadlock_inside_another_exception_is_lost_to_a_deadlock(string sqlState)
    {
        await using var services = OutboxFailingWith(new InvalidOperationException("The transaction could not be committed.", Ended(sqlState)));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.UndefinedTable)]
    public async Task A_commit_postgresql_ended_for_any_other_reason_outside_ef_core_still_throws(string sqlState)
    {
        await using var services = OutboxFailingWith(Ended(sqlState));

        var thrown = await Should.ThrowAsync<PostgresException>(() => new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
        thrown.SqlState.ShouldBe(sqlState);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.UndefinedTable)]
    public async Task A_commit_postgresql_ended_for_any_other_reason_inside_another_exception_still_throws(string sqlState)
    {
        await using var services = OutboxFailingWith(new InvalidOperationException("The transaction could not be committed.", Ended(sqlState)));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
        thrown.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(sqlState);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_save_postgresql_ended_as_a_deadlock_at_one_of_its_statements_is_lost_to_a_deadlock(string sqlState)
    {
        await using var services = OutboxFailingWith(new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.", Ended(sqlState)));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected, Bare)]
    [InlineData(PostgresErrorCodes.SerializationFailure, Bare)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, InsideAnotherException)]
    [InlineData(PostgresErrorCodes.SerializationFailure, InsideAnotherException)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, AtAStatement)]
    [InlineData(PostgresErrorCodes.SerializationFailure, AtAStatement)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, AtTheCommitEfCoreOwns)]
    [InlineData(PostgresErrorCodes.SerializationFailure, AtTheCommitEfCoreOwns)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, BehindAnotherFailureInAnAggregate)]
    [InlineData(PostgresErrorCodes.SerializationFailure, BehindAnotherFailureInAnAggregate)]
    public async Task A_lost_race_is_logged_as_a_warning_naming_its_sql_state_and_context_with_the_exception(string sqlState, string wrapping)
    {
        var ended = Ended(sqlState);
        await using var services = OutboxFailingWith(Wrapped(ended, wrapping));

        await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken);

        // An aggregate's other failure is logged too, on a record of its own; the lost race is the one record about it.
        var record = services.GetFakeLogCollector().GetSnapshot().Where(each => each.Exception is PostgresException).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain(sqlState);
        record.Message.ShouldContain(nameof(EditorialDbContext));
        record.Exception.ShouldBeSameAs(ended);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_commit_postgresql_ended_as_a_deadlock_behind_another_failure_in_an_aggregate_exception_is_lost_to_a_deadlock(string sqlState)
    {
        await using var services = OutboxFailingWith(Wrapped(Ended(sqlState), BehindAnotherFailureInAnAggregate));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An aggregate that holds a lost race beside an unrelated failure still answers as the lost race (409), but the other
    /// failure is logged, at Warning or above, rather than lost with the aggregate.
    /// </summary>
    [Fact]
    public async Task The_other_failures_in_an_aggregate_with_a_lost_race_are_logged_not_lost()
    {
        var other = new InvalidOperationException("The envelopes could not be flushed.");
        await using var services = OutboxFailingWith(new AggregateException(Ended(PostgresErrorCodes.DeadlockDetected), other));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));

        var record = services.GetFakeLogCollector().GetSnapshot().Where(each => ReferenceEquals(each.Exception, other)).ShouldHaveSingleItem();
        record.Level.ShouldBeOneOf(LogLevel.Warning, LogLevel.Error, LogLevel.Critical);
    }

    /// <summary>
    /// The aggregate need not be the top-level exception: one that another exception wraps is searched too, and its
    /// other failure is logged once.
    /// </summary>
    [Fact]
    public async Task The_other_failures_in_an_aggregate_inside_another_exception_with_a_lost_race_are_logged_once()
    {
        var ended = Ended(PostgresErrorCodes.DeadlockDetected);
        var other = new InvalidOperationException("The envelopes could not be flushed.");
        await using var services = OutboxFailingWith(new InvalidOperationException("flush", new AggregateException(ended, other)));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));

        var records = services.GetFakeLogCollector().GetSnapshot();
        var record = records.Where(each => ReferenceEquals(each.Exception, other)).ShouldHaveSingleItem("the other failure inside the wrapped aggregate was not logged once");
        record.Level.ShouldBeOneOf(LogLevel.Warning, LogLevel.Error, LogLevel.Critical);
        records.Select(each => each.Exception).ShouldBe([ended, other], ignoreOrder: true);
    }

    /// <summary>
    /// An aggregate inside an aggregate: every leaf failure but the lost race is logged once, the inner aggregate's
    /// sibling of the lost race as much as the outer one's, and no aggregate is logged as a failure of its own.
    /// </summary>
    [Fact]
    public async Task Every_other_failure_in_nested_aggregates_with_a_lost_race_is_logged_once()
    {
        var ended = Ended(PostgresErrorCodes.DeadlockDetected);
        var siblingA = new InvalidOperationException("The envelopes could not be flushed.");
        var siblingB = new TimeoutException("The outbox did not answer.");
        await using var services = OutboxFailingWith(new AggregateException(new AggregateException(ended, siblingA), siblingB));

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));

        var records = services.GetFakeLogCollector().GetSnapshot();
        records.Where(each => ReferenceEquals(each.Exception, siblingA)).ShouldHaveSingleItem("the lost race's sibling in the inner aggregate was not logged once");
        records.Where(each => ReferenceEquals(each.Exception, siblingB)).ShouldHaveSingleItem("the outer aggregate's other failure was not logged once");
        records.Select(each => each.Exception).ShouldBe([ended, siblingA, siblingB], ignoreOrder: true);
    }

    [Fact]
    public async Task An_aggregate_exception_holding_no_lost_race_still_throws()
    {
        await using var services = OutboxFailingWith(new AggregateException(new InvalidOperationException("The envelopes could not be flushed."), Ended(PostgresErrorCodes.UniqueViolation)));

        var thrown = await Should.ThrowAsync<AggregateException>(() => new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
        thrown.InnerExceptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_save_that_moved_a_row_version_logs_nothing()
    {
        await using var services = OutboxFailingWith(new DbUpdateConcurrencyException("The row version moved on."));

        await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken);

        services.GetFakeLogCollector().GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deadlock_without_a_logger_registered_is_still_lost_to_a_deadlock()
    {
        await using var services = OutboxFailingWith(Ended(PostgresErrorCodes.DeadlockDetected), logging: false);

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    private const string Bare = "bare";

    private const string InsideAnotherException = "inside another exception";

    private const string AtAStatement = "at a statement";

    private const string AtTheCommitEfCoreOwns = "at the commit EF Core owns";

    private const string BehindAnotherFailureInAnAggregate = "behind another failure in an aggregate";

    private static Exception Wrapped(PostgresException ended, string wrapping) => wrapping switch
    {
        Bare => ended,
        InsideAnotherException => new InvalidOperationException("The transaction could not be committed.", ended),
        AtAStatement => new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.", ended),
        AtTheCommitEfCoreOwns => new DbUpdateConcurrencyException("An error occurred while saving the entity changes. See the inner exception for details.", ended),
        BehindAnotherFailureInAnAggregate => new AggregateException(new InvalidOperationException("The envelopes could not be flushed."), ended),
        _ => throw new InvalidOperationException($"No wrapping named '{wrapping}'."),
    };

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_deadlock_the_flush_raises_after_the_commit_is_no_lost_race_and_still_throws(string sqlState)
    {
        var ended = Ended(sqlState);
        await using var services = OutboxFailingWith(failure: null, flushFailure: ended);

        var thrown = await Should.ThrowAsync<PostgresException>(() => new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(ended);
        services.GetFakeLogCollector().GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_lost_a_race_is_never_flushed()
    {
        await using var services = OutboxFailingWith(Ended(PostgresErrorCodes.DeadlockDetected));

        await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken);

        await services.GetRequiredService<IDbContextOutbox<EditorialDbContext>>().DidNotReceive().FlushOutgoingMessagesAsync();
    }

    [Fact]
    public async Task A_save_that_succeeds_is_flushed_once_after_it_and_answers_success()
    {
        await using var services = OutboxFailingWith(failure: null);

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        var outbox = services.GetRequiredService<IDbContextOutbox<EditorialDbContext>>();
        await outbox.Received(1).FlushOutgoingMessagesAsync();
        await outbox.DidNotReceive().SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A registered domain-event scraper runs before the save, on the context it saves and Wolverine's message context,
    /// as Wolverine's own save-and-flush runs it, and the save, commit and flush that follow are split like any other:
    /// so only a failure before the commit is ever a lost race, whatever is registered.
    /// </summary>
    [Fact]
    public async Task A_registered_domain_event_scraper_runs_before_the_save_on_its_context()
    {
        var saves = new List<string>();
        var scraper = Substitute.For<IDomainEventScraper>();
        scraper.ScrapeEvents(Arg.Any<DbContext>(), Arg.Any<MessageContext>()).Returns(_ =>
        {
            saves.Add("scraped");
            return Task.CompletedTask;
        });
        await using var services = WolverineOutbox(new FailingSave(failure: null, saved: () => saves.Add("saved")), scraper);
        var outbox = services.GetRequiredService<IDbContextOutbox<EditorialDbContext>>();

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        saves.ShouldBe(["scraped", "saved"]);
        await scraper.Received(1).ScrapeEvents(outbox.DbContext, (MessageContext)outbox);
    }

    /// <summary>A deadlock in the save that follows a scraper is a lost race to try again, as without one.</summary>
    [Fact]
    public async Task A_deadlock_in_the_save_after_a_scraper_ran_is_a_lost_race_to_a_deadlock()
    {
        await using var services = WolverineOutbox(new FailingSave(Ended(PostgresErrorCodes.DeadlockDetected)), Substitute.For<IDomainEventScraper>());

        ShouldHaveLostTo(Deadlock, await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken));
    }

    private const string Deadlock = "a deadlock";

    private const string VersionChange = "a version that moved on";

    /// <summary>Fails unless the save lost a race, and lost it to <paramref name="expected"/>.</summary>
    private static void ShouldHaveLostTo(string expected, Result<Unit, LostRace> saved)
    {
        saved.IsFailure.ShouldBeTrue("the save answered as committed");
        var lostTo = saved.Error switch
        {
            SaveDeadlocked => Deadlock,
            VersionMovedOn => VersionChange,
        };
        lostTo.ShouldBe(expected);
    }

    private static PostgresException Ended(string sqlState) =>
        new($"the transaction was ended ({sqlState})", "ERROR", "ERROR", sqlState);

    /// <summary>
    /// Editorial's outbox over a real context whose save ends with <paramref name="failure"/> before anything reaches the
    /// database (<see cref="FailingSave"/>); without one, the save succeeds and the flush after it answers <paramref name="flushFailure"/>.
    /// </summary>
    private static ServiceProvider OutboxFailingWith(Exception? failure, bool logging = true, Exception? flushFailure = null)
    {
        var options = new DbContextOptionsBuilder<EditorialDbContext>()
            .UseNpgsql("Host=unused.invalid")
            .AddInterceptors(new FailingSave(failure))
            .Options;
        var services = new ServiceCollection();
        var outbox = Substitute.For<IDbContextOutbox<EditorialDbContext>>();
        services.AddSingleton(outbox);
        if (logging)
        {
            services.AddFakeLogging();
        }

        var provider = services.BuildServiceProvider();
        outbox.DbContext.Returns(new EditorialDbContext(options, provider));
        outbox.FlushOutgoingMessagesAsync().Returns(flushFailure is null ? Task.CompletedTask : Task.FromException(flushFailure));

        // Wolverine's own save-and-flush, which TryCommitAsync falls back to where domain-event scrapers are registered,
        // fails the same way the two steps do.
        var combined = failure ?? flushFailure;
        outbox.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(combined is null ? Task.CompletedTask : Task.FromException(combined));
        return provider;
    }

    /// <summary>
    /// An outbox that is a Wolverine <see cref="MessageContext"/>, as the host resolves (<see cref="WolverineContextOutbox"/>), over a context whose save
    /// <paramref name="save"/> decides, with the scraper registered as the host would register it.
    /// </summary>
    private static ServiceProvider WolverineOutbox(FailingSave save, IDomainEventScraper scraper)
    {
        var services = new ServiceCollection().AddFakeLogging();
        services.AddSingleton(scraper);
        var runtime = Substitute.For<IWolverineRuntime>();
        runtime.Options.Returns(new WolverineOptions());
        var provider = services.AddSingleton<IDbContextOutbox<EditorialDbContext>>(container => new WolverineContextOutbox(
                runtime,
                new EditorialDbContext(new DbContextOptionsBuilder<EditorialDbContext>().UseNpgsql("Host=unused.invalid").AddInterceptors(save).Options, container)))
            .BuildServiceProvider();
        return provider;
    }
}
