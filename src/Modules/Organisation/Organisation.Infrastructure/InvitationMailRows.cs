using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// Creates a row an invitation mail counts on when it is missing, and holds it until the context's transaction ends
/// (docs/adr/0004, #369, #375). The transaction is begun here when none is open; the unit of work commits it, and the
/// command's domain events wait for that commit. A command that does not commit (a refusal, a lost race) never
/// commits it either: the transaction, with its locks and any row it inserted, is rolled back when the request's scope
/// disposes the context.
/// <para>
/// Every statement of the transaction from here on waits at most <see cref="LockTimeout"/> for a lock
/// (<c>SET LOCAL lock_timeout</c>, so it ends with the transaction and no other use of the pooled connection keeps it).
/// A wait past it fails with 55P03 (<c>lock_not_available</c>): here it is a lost race, <see cref="SaveDeadlocked"/>,
/// and at the save the unit of work answers it the same way; the caller then commits nothing and may try again.
/// </para>
/// </summary>
internal static class InvitationMailRows
{
    /// <summary>The longest a statement of the transaction waits for a lock, as PostgreSQL spells it.</summary>
    public const string LockTimeout = "5s";

    private const string SetLockTimeout = "SET LOCAL lock_timeout = '" + LockTimeout + "'";

    /// <summary>
    /// Runs <paramref name="insertIfMissing"/> (an <c>INSERT … ON CONFLICT DO NOTHING</c>) and then
    /// <paramref name="hold"/> (a <c>SELECT 1 AS "Value" … FOR UPDATE</c> of that row) in the context's transaction. A
    /// second transaction for the same row waits at the insert, for the row the first is inserting, or at the lock, and
    /// goes on with the row as the first committed it.
    /// </summary>
    public static async Task<Result<Unit, LostRace>> CreateAndHoldAsync(
        OrganisationDbContext context,
        FormattableString insertIfMissing,
        FormattableString hold,
        CancellationToken cancellationToken)
    {
        Ensure.That(context).IsNotNull();
        Ensure.That(insertIfMissing).IsNotNull();
        Ensure.That(hold).IsNotNull();

        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            await context.Database.ExecuteSqlRawAsync(SetLockTimeout, cancellationToken);
            await context.Database.ExecuteSqlAsync(insertIfMissing, cancellationToken);
            await context.Database.SqlQuery<int>(hold).ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (TimedOut(exception) is { } timedOut)
        {
            return Failure(LostRaces.Deadlocked(timedOut.SqlState));
        }

        return Success(Unit.Value);
    }

    /// <summary>The 55P03 that ended a statement's wait for a lock, however it is wrapped; none for any other failure.</summary>
    private static PostgresException? TimedOut(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } timedOut => timedOut,
        { InnerException: { } inner } => TimedOut(inner),
        _ => null,
    };
}
