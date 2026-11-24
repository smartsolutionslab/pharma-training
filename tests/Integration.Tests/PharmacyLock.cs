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

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
