using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// <c>ModuleUnitOfWork.TryCommitAsync</c>, through Editorial's unit of work, without a database. When PostgreSQL ends a
/// save as a deadlock (40P01) or a serialization failure (40001), even at the COMMIT that SaveChangesAsync issues for
/// its own transaction (where a deferred key check closes the cycle), EFCore.PG reports it as a
/// <see cref="DbUpdateConcurrencyException"/> around the <see cref="PostgresException"/>; observed against PostgreSQL
/// for EFCore.PG 11.0.0-rc.3. That is a lost race, so the save answers <see langword="false"/>, as it does for a 40P01 or
/// 40001 raised by one of the save's statements, which EF Core wraps in a plain <see cref="DbUpdateException"/>. A
/// database error with any other SqlState still throws.
/// <para>
/// These tests build the <see cref="DbUpdateConcurrencyException"/> by hand, so the 40P01 or 40001 inside it plays no
/// part in them: only the integration test
/// <c>A_question_added_to_a_chapter_whose_removal_deadlocks_with_it_at_commit_is_told_the_version_changed_never_a_server_error</c>
/// (<c>EditorialQuestionRacesTests</c>) pins that the provider wraps a commit-time deadlock that way.
/// </para>
/// <para>
/// When a handler publishes, Wolverine's EF Core envelope transaction commits through
/// <c>Database.CommitTransactionAsync</c> instead, and the same failure arrives as a bare <see cref="PostgresException"/>
/// (or inside another exception), with no <see cref="DbUpdateConcurrencyException"/> around it. That is a lost race
/// too, so it answers <see langword="false"/>; a <see cref="PostgresException"/> with any other SqlState still throws.
/// </para>
/// </summary>
public class CommitLostRaceTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_save_postgresql_ended_as_a_lost_race_at_its_commit_answers_false(string sqlState)
    {
        await using var services = OutboxFailingWith(new DbUpdateConcurrencyException("An error occurred while saving the entity changes. See the inner exception for details.", Ended(sqlState)));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_save_that_moved_a_row_version_answers_false()
    {
        await using var services = OutboxFailingWith(new DbUpdateConcurrencyException("The row version moved on."));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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
    public async Task A_commit_postgresql_ended_as_a_lost_race_outside_ef_core_answers_false(string sqlState)
    {
        await using var services = OutboxFailingWith(Ended(sqlState));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_commit_postgresql_ended_as_a_lost_race_inside_another_exception_answers_false(string sqlState)
    {
        await using var services = OutboxFailingWith(new InvalidOperationException("The transaction could not be committed.", Ended(sqlState)));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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
    public async Task A_save_postgresql_ended_as_a_lost_race_at_one_of_its_statements_answers_false(string sqlState)
    {
        await using var services = OutboxFailingWith(new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.", Ended(sqlState)));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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
    public async Task A_commit_postgresql_ended_as_a_lost_race_behind_another_failure_in_an_aggregate_exception_answers_false(string sqlState)
    {
        await using var services = OutboxFailingWith(Wrapped(Ended(sqlState), BehindAnotherFailureInAnAggregate));

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

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

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

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

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

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
    public async Task A_lost_race_without_a_logger_registered_still_answers_false()
    {
        await using var services = OutboxFailingWith(Ended(PostgresErrorCodes.DeadlockDetected), logging: false);

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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
    public async Task A_save_that_succeeds_is_flushed_once_after_it_and_answers_true()
    {
        await using var services = OutboxFailingWith(failure: null);

        (await new EditorialUnitOfWork(services).TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        var outbox = services.GetRequiredService<IDbContextOutbox<EditorialDbContext>>();
        await outbox.Received(1).FlushOutgoingMessagesAsync();
        await outbox.DidNotReceive().SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
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
}
