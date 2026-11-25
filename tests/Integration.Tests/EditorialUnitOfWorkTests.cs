using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// <see cref="EditorialUnitOfWork.RollbackAsync"/> against PostgreSQL (#462): after a COMMIT that failed, EF Core keeps
/// the transaction as the context's current one, so the rollback must get rid of it, or the command's next read and the
/// next person's hold run in a transaction the server has already ended. A COMMIT is made to fail by a unique constraint
/// checked only at the commit (<c>DEFERRABLE INITIALLY DEFERRED</c>) on a temporary table holding duplicates.
/// </summary>
public class EditorialUnitOfWorkTests(AspireFixture aspire)
{
    private const string DuplicatesCheckedAtCommit =
        """
        CREATE TEMP TABLE failing_commit (value integer UNIQUE DEFERRABLE INITIALLY DEFERRED) ON COMMIT DROP;
        INSERT INTO failing_commit (value) VALUES (1), (1);
        """;

    [Fact]
    public async Task A_rollback_after_a_failed_commit_clears_the_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<EditorialDbContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EditorialDbContext>();
        var unitOfWork = new EditorialUnitOfWork(scope.ServiceProvider);

        await new ErasedPersonStore(context).HoldAsync(PersonId.From(Guid.CreateVersion7()), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(DuplicatesCheckedAtCommit, cancellationToken);
        var failed = await Should.ThrowAsync<PostgresException>(() => context.Database.CommitTransactionAsync(cancellationToken));
        failed.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation, "the commit must fail at the deferred check, or the test does not exercise the path it guards");

        await Should.NotThrowAsync(() => unitOfWork.RollbackAsync(cancellationToken));

        context.Database.CurrentTransaction.ShouldBeNull("a failed COMMIT's transaction must not stay the context's");
        await new ErasedPersonStore(context).HoldAsync(PersonId.From(Guid.CreateVersion7()), cancellationToken);
        context.Database.CurrentTransaction.ShouldNotBeNull("the next hold begins a transaction of its own");
        await context.Database.RollbackTransactionAsync(cancellationToken);
    }
}
