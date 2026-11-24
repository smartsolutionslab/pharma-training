using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A transaction a test holds on rows or a table (a pharmacy's row and its memberships' rows, or the invitation
/// recipients' table); committing it lets the requests waiting on them go on.
/// </summary>
public sealed class PharmacyLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    /// <summary>The backend that holds the rows, which the requests waiting on them wait for.</summary>
    public int ProcessId => connection.ProcessID;

    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    /// <summary>
    /// Asks, inside the gate's transaction, for a lock on the service's durable inbox
    /// (<c>wolverine.wolverine_incoming_envelopes</c>) that waits for every transaction holding a row it wrote there. A
    /// request that stored its envelope there and then waits for the gate's rows closes a deadlock with it; returns once
    /// PostgreSQL has ended one of the two, and throws its <see cref="PostgresException"/> when the one ended was the gate.
    /// </summary>
    public async Task LockIncomingEnvelopesAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("LOCK TABLE wolverine.wolverine_incoming_envelopes IN SHARE MODE", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
