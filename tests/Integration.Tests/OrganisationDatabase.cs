using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The Organisation service's own database, read and written by the tests directly where the API cannot reach yet:
/// a second member of a pharmacy exists only once invitations can be accepted.
/// </summary>
internal sealed class OrganisationDatabase(AspireFixture aspire)
{
    /// <summary>
    /// Seeds what accepting an invitation (#10) will do through the API; #10 replaces this seed. The row is the one the
    /// domain writes for an accepted invitation: the realm user's subject, name and address, no token, active since now.
    /// </summary>
    public async Task<Guid> SeedMemberAsync(Guid pharmacy, string user, string role, string jobFunction, CancellationToken cancellationToken)
    {
        using var claims = await ClaimsOfAsync(user);
        var membership = Guid.CreateVersion7();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO organisation.memberships (id, pharmacy_id, email, token, invitation_expires_at, person_id, name, job_function, role, status, started_at, ended_at)
            VALUES (@id, @pharmacy, @email, NULL, NULL, @person, @name, @jobFunction, @role, 'Active', now(), NULL)
            """,
            connection);
        command.Parameters.AddWithValue("id", membership);
        command.Parameters.AddWithValue("pharmacy", pharmacy);
        // Trimmed and lower-cased, as EmailAddress.From writes it: the row must be one the domain could have written.
        command.Parameters.AddWithValue("email", (claims.RootElement.GetProperty("email").GetString() ?? throw new InvalidOperationException($"{user} has no email.")).Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("person", Guid.Parse(claims.RootElement.GetProperty("sub").GetString() ?? throw new InvalidOperationException($"{user} has no subject.")));
        command.Parameters.AddWithValue("name", claims.RootElement.GetProperty("name").GetString() ?? throw new InvalidOperationException($"{user} has no name."));
        command.Parameters.AddWithValue("jobFunction", jobFunction);
        command.Parameters.AddWithValue("role", role);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        return membership;
    }

    /// <summary>The realm user's Keycloak subject: the person a membership belongs to.</summary>
    public async Task<Guid> SubjectOfAsync(string user)
    {
        using var claims = await ClaimsOfAsync(user);
        return Guid.Parse(claims.RootElement.GetProperty("sub").GetString() ?? throw new InvalidOperationException($"{user} has no subject."));
    }

    /// <summary>The membership's status, role, job function and end as stored.</summary>
    public async Task<StoredMembership> MembershipAsync(Guid membership, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT status, role, job_function, ended_at FROM organisation.memberships WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", membership);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"There is no membership {membership}.");
        return new StoredMembership(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>The number of the pharmacy's memberships that are active and hold the role Manager.</summary>
    public async Task<long> ActiveManagersAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM organisation.memberships WHERE pharmacy_id = @pharmacy AND status = 'Active' AND role = 'Manager'",
            connection);
        command.Parameters.AddWithValue("pharmacy", pharmacy);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    /// <summary>The pharmacy row's PostgreSQL row version (<c>xmin</c>), which every update of the row moves on.</summary>
    public async Task<string> RowVersionAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT xmin::text FROM organisation.pharmacies WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", pharmacy);
        return (string)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException($"There is no pharmacy {pharmacy}."));
    }

    /// <summary>
    /// Opens a transaction that holds the pharmacy's row and its memberships' rows, so every request that writes them
    /// waits after it has loaded the pharmacy until the transaction ends. <c>FOR UPDATE</c> locks the rows without
    /// moving their row version, so what the requests loaded stays current. Disposing the lock closes its connection.
    /// </summary>
    public async Task<PharmacyLock> LockPharmacyAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var row = new NpgsqlCommand("SELECT 1 FROM organisation.pharmacies WHERE id = @id FOR UPDATE", connection, transaction))
            {
                row.Parameters.AddWithValue("id", pharmacy);
                (await row.ExecuteScalarAsync(cancellationToken)).ShouldNotBeNull();
            }

            await using (var memberships = new NpgsqlCommand("SELECT 1 FROM organisation.memberships WHERE pharmacy_id = @id FOR UPDATE", connection, transaction))
            {
                memberships.Parameters.AddWithValue("id", pharmacy);
                await memberships.ExecuteNonQueryAsync(cancellationToken);
            }

            return new PharmacyLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Polls the organisation database until the given number of statements on the module's tables wait for a lock,
    /// and fails the test when they do not within 30 seconds.
    /// </summary>
    public async Task WaitUntilBlockedAsync(int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE wait_event_type = 'Lock' AND datname = current_database() AND pid <> pg_backend_pid()
              AND query ILIKE '%organisation.%'
            """,
            connection);

        var blocked = 0L;
        while (blocked < expected)
        {
            if (timeout.IsCancellationRequested)
            {
                blocked.ShouldBe(expected, "the concurrent requests never waited for the lock on the pharmacy");
            }

            blocked = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (blocked < expected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }

        blocked.ShouldBe(expected);
    }

    private async Task<JsonDocument> ClaimsOfAsync(string user)
    {
        var token = await aspire.RequestAccessTokenAsync(user);
        var payload = Base64Url.DecodeFromChars(token.Split('.')[1]);
        return JsonDocument.Parse(Encoding.UTF8.GetString(payload));
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("organisation-database", cancellationToken)
            ?? throw new InvalidOperationException("The organisation database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>A membership row as the tests read it back.</summary>
    internal sealed record StoredMembership(string Status, string Role, string JobFunction, DateTimeOffset? EndedAt);
}
