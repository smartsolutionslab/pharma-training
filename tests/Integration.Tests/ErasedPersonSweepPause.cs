using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Holds off a service's sweep of erased persons' messages (#439) while a test waits for a membership message to be
/// handled. A tombstoned person's message is consumed, dropped and marked Handled, and the sweep, every
/// <see cref="AspireFixture.ErasedPersonMessageSweepInterval"/>, may delete that handled envelope before a poll ever sees
/// it, so a barrier on it would wait for a row that is gone. A pass takes the sweep's advisory lock first and deletes
/// nothing while another session holds it, as it does for another replica of the service; this holds that lock shared,
/// so pauses never wait for each other, and releases it when disposed. It changes nothing a test proves: the message is
/// still handled, never dead-lettered, and the sweep removes its envelope on the first pass after the pause.
/// </summary>
internal sealed class ErasedPersonSweepPause : IAsyncDisposable
{
    /// <summary>The key of the advisory lock every pass takes in <c>PersonalMessagePurge.PurgeErasedAsync</c>.</summary>
    private const string Key = "hashtextextended('wolverine.erased-person-sweep', 0)";

    private static readonly string[] SweptTypes =
    [
        "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1",
        "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1",
    ];

    private readonly NpgsqlConnection? connection;

    private ErasedPersonSweepPause(NpgsqlConnection? connection)
    {
        this.connection = connection;
    }

    /// <summary>
    /// Pauses the sweep in the service's database, <c>&lt;service&gt;-database</c>, when the sweep could remove a message of
    /// <paramref name="type"/>; any other type is never swept, and nothing is paused for it.
    /// </summary>
    public static async Task<ErasedPersonSweepPause> StartAsync(AspireFixture aspire, string service, string type, CancellationToken cancellationToken)
    {
        if (!SweptTypes.Contains(type, StringComparer.Ordinal))
        {
            return new ErasedPersonSweepPause(null);
        }

        var connectionString = await aspire.App.GetConnectionStringAsync($"{service}-database", cancellationToken)
            ?? throw new InvalidOperationException($"{service}'s database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_lock_shared({Key})", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new ErasedPersonSweepPause(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is null)
        {
            return;
        }

        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_unlock_shared({Key})", connection))
        {
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await connection.DisposeAsync();
    }
}
