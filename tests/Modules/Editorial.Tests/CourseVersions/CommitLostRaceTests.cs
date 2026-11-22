using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// <c>ModuleUnitOfWork.TryCommitAsync</c>, through Editorial's unit of work, without a database. When PostgreSQL ends a
/// save as a deadlock (40P01) or a serialization failure (40001), even at the COMMIT that SaveChangesAsync issues for
/// its own transaction (where a deferred key check closes the cycle), EFCore.PG reports it as a
/// <see cref="DbUpdateConcurrencyException"/> around the <see cref="PostgresException"/>; observed against PostgreSQL
/// for EFCore.PG 11.0.0-rc.3. That is a lost race, so the save answers <see langword="false"/>. Any other database error
/// still throws.
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

    private static PostgresException Ended(string sqlState) =>
        new($"the transaction was ended ({sqlState})", "ERROR", "ERROR", sqlState);

    private static ServiceProvider OutboxFailingWith(Exception failure)
    {
        var outbox = Substitute.For<IDbContextOutbox<EditorialDbContext>>();
        outbox.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        return new ServiceCollection().AddSingleton(outbox).BuildServiceProvider();
    }
}
