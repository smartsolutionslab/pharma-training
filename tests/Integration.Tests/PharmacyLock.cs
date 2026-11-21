using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A transaction a test holds on a pharmacy's row and its memberships' rows; committing it lets the requests waiting
/// on them go on.
/// </summary>
public sealed class PharmacyLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
