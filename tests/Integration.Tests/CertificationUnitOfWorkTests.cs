using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// <see cref="CertificationUnitOfWork.AbandonAsync"/> against PostgreSQL (#462): it rolls back the person's hold (#115),
/// gets rid of the transaction even after a COMMIT that failed, and forgets every tracked change, so the purge goes on
/// with the next person in a transaction of their own. A COMMIT is made to fail by a unique constraint checked only at
/// the commit (<c>DEFERRABLE INITIALLY DEFERRED</c>) on a temporary table holding duplicates in the same transaction.
/// </summary>
public class CertificationUnitOfWorkTests(AspireFixture aspire)
{
    private const string DuplicatesCheckedAtCommit =
        """
        CREATE TEMP TABLE failing_commit (value integer UNIQUE DEFERRABLE INITIALLY DEFERRED) ON COMMIT DROP;
        INSERT INTO failing_commit (value) VALUES (1), (1);
        """;

    [Fact]
    public async Task Abandoning_after_a_failed_commit_clears_the_transaction_and_the_tracked_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await ServicesAsync(cancellationToken);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CertificationDbContext>();
        var unitOfWork = new CertificationUnitOfWork(scope.ServiceProvider);
        var person = PersonId.From(Guid.CreateVersion7());

        await new ErasedPersonStore(context).HoldAsync(person, cancellationToken);
        context.Add(Started(person));
        await context.Database.ExecuteSqlRawAsync(DuplicatesCheckedAtCommit, cancellationToken);
        var failed = await Should.ThrowAsync<PostgresException>(() => context.Database.CommitTransactionAsync(cancellationToken));
        failed.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation, "the commit must fail at the deferred check, or the test does not exercise the path it guards");

        await Should.NotThrowAsync(() => unitOfWork.AbandonAsync(cancellationToken));

        context.Database.CurrentTransaction.ShouldBeNull("a failed COMMIT's transaction must not stay the context's for the next person");
        context.ChangeTracker.Entries().ShouldBeEmpty();
        await new ErasedPersonStore(context).HoldAsync(PersonId.From(Guid.CreateVersion7()), cancellationToken);
        context.Database.CurrentTransaction.ShouldNotBeNull("the next person's hold begins a transaction of its own");
        await context.Database.RollbackTransactionAsync(cancellationToken);
    }

    [Fact]
    public async Task Abandoning_an_open_hold_rolls_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await ServicesAsync(cancellationToken);
        var person = PersonId.From(Guid.CreateVersion7());
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CertificationDbContext>();
            var unitOfWork = new CertificationUnitOfWork(scope.ServiceProvider);
            await new ErasedPersonStore(context).HoldAsync(person, cancellationToken);
            context.Add(Started(person));
            await context.SaveChangesAsync(cancellationToken);

            await unitOfWork.AbandonAsync(cancellationToken);

            context.Database.CurrentTransaction.ShouldBeNull();
            context.ChangeTracker.Entries().ShouldBeEmpty();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var fresh = scope.ServiceProvider.GetRequiredService<CertificationDbContext>();
            (await fresh.Set<Membership>().AnyAsync(membership => membership.Person == person, cancellationToken))
                .ShouldBeFalse("the membership saved in the abandoned hold is rolled back with it");
        }
    }

    private static Membership Started(PersonId person) =>
        Membership.Started(
            MembershipId.From(Guid.CreateVersion7()),
            person,
            PharmacyId.From(Guid.CreateVersion7()),
            HolderName.From("Anna Berger"),
            MembershipRole.Employee,
            Timestamp.From(TimeProvider.System.GetUtcNow()));

    /// <summary>
    /// The context on Certification's real database, scoped as the host scopes it, with no reactions registered: the
    /// unit of work resolves the very context the stores use from the scope.
    /// </summary>
    private async Task<ServiceProvider> ServicesAsync(CancellationToken cancellationToken)
    {
        var connectionString = await CertificationDatabase.ConnectionStringAsync(aspire, cancellationToken);
        return new ServiceCollection()
            .AddLogging()
            .AddDbContext<CertificationDbContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();
    }
}
