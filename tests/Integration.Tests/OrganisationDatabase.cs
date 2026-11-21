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
        command.Parameters.AddWithValue("email", claims.RootElement.GetProperty("email").GetString() ?? throw new InvalidOperationException($"{user} has no email."));
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
