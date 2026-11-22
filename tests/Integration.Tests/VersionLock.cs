using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A transaction a test holds on a course version's rows; committing or rolling it back lets the requests waiting on them go on.
/// </summary>
public sealed class VersionLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
