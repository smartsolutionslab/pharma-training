using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using NpgsqlTypes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The Organisation service's own database, read and written by the tests directly where the API cannot reach yet:
/// a second member of a pharmacy exists only once invitations can be accepted.
/// </summary>
internal sealed class OrganisationDatabase(AspireFixture aspire)
{
    private const string MailRequestType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.InvitationMailRequestedV1";

    /// <summary>The link hashes of the membership aliased <c>m</c>, in hash order, as one array: empty when it keeps none.</summary>
    private const string LinkHashes = "array(SELECT l.token_hash FROM organisation.invitation_links l WHERE l.membership_id = m.id ORDER BY l.token_hash)";

    /// <summary>
    /// Seeds, fast and without the broker, the row accepting an invitation writes, for tests that are not about accepting;
    /// accepting itself is tested end to end in <see cref="OrganisationAcceptInvitationTests"/>. The row is the one the
    /// domain writes for an accepted invitation: the realm user's subject, name and address, no token, active since now.
    /// </summary>
    public async Task<Guid> SeedMemberAsync(Guid pharmacy, string user, string role, string jobFunction, CancellationToken cancellationToken)
    {
        using var claims = await ClaimsOfAsync(user);
        var membership = Guid.CreateVersion7();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO organisation.memberships (id, pharmacy_id, email, invitation_expires_at, person_id, name, job_function, role, status, started_at, ended_at)
            VALUES (@id, @pharmacy, @email, NULL, @person, @name, @jobFunction, @role, 'Active', now(), NULL)
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

    /// <summary>
    /// Writes an invitation row by hand, past the aggregate, as a second writer would: what the database itself refuses
    /// is what these rows show. It expires 14 days from now unless <paramref name="expiresAt"/> says otherwise. No mail
    /// is requested for it, so nothing replaces the hash: a test that keeps the token can accept it. The row and its one
    /// link are written in one statement, so a link the database refuses leaves no row behind.
    /// </summary>
    public async Task<Guid> InsertInvitationAsync(
        Guid pharmacy,
        string email,
        string tokenHash,
        string status,
        CancellationToken cancellationToken,
        DateTimeOffset? expiresAt = null,
        string role = "Employee",
        string jobFunction = "Pta")
    {
        var membership = Guid.CreateVersion7();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH membership AS (
                INSERT INTO organisation.memberships (id, pharmacy_id, email, invitation_expires_at, person_id, name, job_function, role, status, started_at, ended_at)
                VALUES (@id, @pharmacy, @email, coalesce(@expiresAt, now() + interval '14 days'), NULL, NULL, @jobFunction, @role, @status, NULL, NULL)
                RETURNING id)
            INSERT INTO organisation.invitation_links (membership_id, token_hash)
            SELECT id, @tokenHash FROM membership
            """,
            connection);
        command.Parameters.AddWithValue("id", membership);
        command.Parameters.AddWithValue("pharmacy", pharmacy);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.Add(new NpgsqlParameter("expiresAt", NpgsqlDbType.TimestampTz) { Value = expiresAt is { } at ? at.ToUniversalTime() : DBNull.Value });
        command.Parameters.AddWithValue("jobFunction", jobFunction);
        command.Parameters.AddWithValue("role", role);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        return membership;
    }

    /// <summary>
    /// Writes a membership row of a person by hand, past the aggregate, as a second writer would: <c>Active</c> since
    /// now, or <c>Ended</c> now. What the database itself refuses is what these rows show.
    /// </summary>
    public async Task<Guid> InsertMembershipOfPersonAsync(Guid pharmacy, Guid person, string email, string status, CancellationToken cancellationToken)
    {
        var membership = Guid.CreateVersion7();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO organisation.memberships (id, pharmacy_id, email, invitation_expires_at, person_id, name, job_function, role, status, started_at, ended_at)
            VALUES (@id, @pharmacy, @email, NULL, @person, 'Anna Employee', 'Pta', 'Employee', @status, now(), CASE WHEN @status = 'Ended' THEN now() END)
            """,
            connection);
        command.Parameters.AddWithValue("id", membership);
        command.Parameters.AddWithValue("pharmacy", pharmacy);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("person", person);
        command.Parameters.AddWithValue("status", status);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        return membership;
    }

    /// <summary>
    /// Clears the membership's mailed mark, as if its version had never been mailed, so a mail request that reaches the
    /// consumer afterwards issues a link and mails it instead of being skipped as already mailed.
    /// </summary>
    public async Task ClearInvitationMailedAsync(Guid membership, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE organisation.memberships SET invitation_mailed_for = NULL WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", membership);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"There is no membership {membership}.");
    }

    /// <summary>
    /// Time travel for the renew cooldown: moves the instant the membership's newest invitation mail was requested
    /// back by the span given, as if that request had been made so much earlier.
    /// </summary>
    public async Task MoveInvitationRequestBackAsync(Guid membership, TimeSpan by, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE organisation.memberships SET invitation_requested_at = invitation_requested_at - @by WHERE id = @id AND invitation_requested_at IS NOT NULL",
            connection);
        command.Parameters.AddWithValue("id", membership);
        command.Parameters.AddWithValue("by", by);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"There is no membership {membership} whose invitation mail was requested.");
    }

    /// <summary>Sets the pharmacy's tally of invitation mails to the count given, all of them requested today (UTC).</summary>
    public async Task SetInvitationMailsTodayAsync(Guid pharmacy, int count, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE organisation.pharmacies SET invitation_mail_day = (now() at time zone 'utc')::date, invitation_mails_that_day = @count WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", pharmacy);
        command.Parameters.AddWithValue("count", count);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"There is no pharmacy {pharmacy}.");
    }

    /// <summary>Time travel for the daily cap: moves the day of the pharmacy's tally back by one, as if its mails had been requested yesterday (UTC).</summary>
    public async Task MoveInvitationMailDayBackAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE organisation.pharmacies SET invitation_mail_day = invitation_mail_day - 1 WHERE id = @id AND invitation_mail_day IS NOT NULL",
            connection);
        command.Parameters.AddWithValue("id", pharmacy);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"There is no pharmacy {pharmacy} that has counted an invitation mail.");
    }

    /// <summary>The number of the pharmacy's memberships, whatever their status.</summary>
    public async Task<long> MembershipsOfAsync(Guid pharmacy, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM organisation.memberships WHERE pharmacy_id = @id", connection);
        command.Parameters.AddWithValue("id", pharmacy);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    /// <summary>The token hashes of the membership's stored links, in hash order; empty when it keeps none.</summary>
    public async Task<List<string>> TokenHashesAsync(Guid membership, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {LinkHashes} FROM organisation.memberships m WHERE m.id = @id", connection);
        command.Parameters.AddWithValue("id", membership);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"There is no membership {membership}.");
        return [.. reader.GetFieldValue<string[]>(0)];
    }

    /// <summary>The statuses of the pharmacy's memberships of the address, oldest first.</summary>
    public async Task<List<string>> StatusesOfAsync(Guid pharmacy, string email, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT status FROM organisation.memberships WHERE pharmacy_id = @pharmacy AND email = @email ORDER BY id",
            connection);
        command.Parameters.AddWithValue("pharmacy", pharmacy);
        command.Parameters.AddWithValue("email", email);
        List<string> statuses = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            statuses.Add(reader.GetString(0));
        }

        return statuses;
    }

    /// <summary>The realm user's address, as the domain stores it: trimmed and lower-case.</summary>
    public async Task<string> EmailOfAsync(string user)
    {
        using var claims = await ClaimsOfAsync(user);
        return (claims.RootElement.GetProperty("email").GetString() ?? throw new InvalidOperationException($"{user} has no email.")).Trim().ToLowerInvariant();
    }

    /// <summary>The realm user's Keycloak subject: the person a membership belongs to.</summary>
    public async Task<Guid> SubjectOfAsync(string user)
    {
        using var claims = await ClaimsOfAsync(user);
        return Guid.Parse(claims.RootElement.GetProperty("sub").GetString() ?? throw new InvalidOperationException($"{user} has no subject."));
    }

    /// <summary>The realm user's display name, as accepting an invitation stores it.</summary>
    public async Task<string> NameOfAsync(string user)
    {
        using var claims = await ClaimsOfAsync(user);
        return claims.RootElement.GetProperty("name").GetString() ?? throw new InvalidOperationException($"{user} has no name.");
    }

    /// <summary>The person's active memberships in every pharmacy, oldest first.</summary>
    public async Task<List<ActiveMembership>> ActiveMembershipsOfAsync(Guid person, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id, pharmacy_id FROM organisation.memberships WHERE person_id = @person AND status = 'Active' ORDER BY id",
            connection);
        command.Parameters.AddWithValue("person", person);
        List<ActiveMembership> memberships = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            memberships.Add(new ActiveMembership(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return memberships;
    }

    /// <summary>The membership's status, person, name and link hashes as stored: what accepting an invitation writes.</summary>
    public async Task<StoredMember> MemberAsync(Guid membership, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT status, person_id, name, {LinkHashes} FROM organisation.memberships m WHERE m.id = @id",
            connection);
        command.Parameters.AddWithValue("id", membership);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"There is no membership {membership}.");
        return new StoredMember(
            reader.GetString(0),
            await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetGuid(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
            [.. reader.GetFieldValue<string[]>(3)]);
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

    /// <summary>The number of pharmacies stored under the name.</summary>
    public async Task<long> PharmaciesNamedAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM organisation.pharmacies WHERE name = @name", connection);
        command.Parameters.AddWithValue("name", name);
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
    /// Polls the organisation database until the given number of backends wait for the gate, directly or behind each
    /// other, and fails the test when they do not within 30 seconds. Other tests run against the same database at the
    /// same time, so a backend counts only when its wait leads back to the gate's own backend: a second request that
    /// waits for a row behind the first is blocked by the first, not by the gate.
    /// </summary>
    public async Task WaitUntilBlockedAsync(PharmacyLock gate, int expected, CancellationToken cancellationToken)
    {
        Ensure.That(gate).IsNotNull();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH RECURSIVE behind(pid) AS (
                SELECT activity.pid FROM pg_stat_activity activity WHERE @gate = ANY(pg_blocking_pids(activity.pid))
                UNION
                SELECT activity.pid FROM pg_stat_activity activity JOIN behind ON behind.pid = ANY(pg_blocking_pids(activity.pid))
            )
            SELECT count(*) FROM behind
            """,
            connection);
        command.Parameters.AddWithValue("gate", gate.ProcessId);

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

    /// <summary>
    /// Polls the service's durable inbox (<c>wolverine.wolverine_incoming_envelopes</c>) until it holds the mail request
    /// of the membership, found by its message type and the membership's id in its body, and answers the envelope; fails
    /// the test when there is none within 30 seconds. Wolverine keeps a handled envelope for a while, so it is found also
    /// once the mail is out.
    /// </summary>
    public Task<MailRequestEnvelope> MailRequestEnvelopeAsync(Guid membership, CancellationToken cancellationToken) =>
        MailRequestEnvelopeAsync(membership, minimumAttempts: 0, TimeSpan.FromSeconds(30), cancellationToken);

    /// <summary>As above, once the envelope has been tried at least <paramref name="minimumAttempts"/> times.</summary>
    public async Task<MailRequestEnvelope> MailRequestEnvelopeAsync(Guid membership, int minimumAttempts, TimeSpan within, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT id, received_at, attempts, body FROM wolverine.wolverine_incoming_envelopes
                WHERE message_type = @type AND position(convert_to(@membership, 'UTF8') in body) > 0
                """,
                connection);
            command.Parameters.AddWithValue("type", MailRequestType);
            command.Parameters.AddWithValue("membership", membership.ToString());
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    var envelope = new MailRequestEnvelope(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), MessageJson(reader.GetFieldValue<byte[]>(3)));
                    if (envelope.Attempts >= minimumAttempts)
                    {
                        return envelope;
                    }
                }
            }

            if (TimeProvider.System.GetElapsedTime(started) >= within)
            {
                throw new TimeoutException($"no envelope of InvitationMailRequestedV1 for {membership} with at least {minimumAttempts} attempt(s) within {within.TotalSeconds:0} s");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    /// <summary>
    /// Polls the service's dead letters (<c>wolverine.wolverine_dead_letters</c>) until it holds the mail request of the
    /// membership, and answers the row: its id, its exception message, its message's JSON and the whole row as JSON text
    /// with the body escaped. Fails the test when there is none within <paramref name="within"/>.
    /// </summary>
    public async Task<MailRequestDeadLetter> MailRequestDeadLetterAsync(Guid membership, TimeSpan within, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT id, exception_message, body,
                       (to_jsonb(dead_letter) - 'body' || jsonb_build_object('body', encode(dead_letter.body, 'escape')))::text
                FROM wolverine.wolverine_dead_letters dead_letter
                WHERE message_type = @type AND position(convert_to(@membership, 'UTF8') in body) > 0
                """,
                connection);
            command.Parameters.AddWithValue("type", MailRequestType);
            command.Parameters.AddWithValue("membership", membership.ToString());
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new MailRequestDeadLetter(
                        reader.GetGuid(0),
                        await reader.IsDBNullAsync(1, cancellationToken) ? string.Empty : reader.GetString(1),
                        MessageJson(reader.GetFieldValue<byte[]>(2)),
                        reader.GetString(3));
                }
            }

            if (TimeProvider.System.GetElapsedTime(started) >= within)
            {
                throw new TimeoutException($"Organisation had not dead-lettered the mail request of {membership} after {within.TotalSeconds:0} s");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    /// <summary>
    /// The exception message of the membership's dead-lettered mail request, or null while
    /// <c>wolverine.wolverine_dead_letters</c> holds none for it; asks once, without waiting.
    /// </summary>
    public async Task<string?> MailRequestDeadLetterReasonAsync(Guid membership, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(exception_message, '') FROM wolverine.wolverine_dead_letters
            WHERE message_type = @type AND position(convert_to(@membership, 'UTF8') in body) > 0
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("type", MailRequestType);
        command.Parameters.AddWithValue("membership", membership.ToString());
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>Marks the dead letter replayable, so Wolverine delivers it again.</summary>
    public async Task ReplayAsync(Guid deadLetter, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE wolverine.wolverine_dead_letters SET replayable = true WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", deadLetter);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// The message's JSON inside a stored envelope. Wolverine stores the whole envelope in <c>body</c>
    /// (<c>EnvelopeSerializer</c>: length-prefixed headers, then the message's bytes), so it is no UTF-8 text as a whole.
    /// </summary>
    /// <summary>
    /// The JSON message inside Wolverine's binary envelope: the first "{" from which the bytes up to the last "}" parse as
    /// one JSON object. The headers before it are binary, and a byte of a Guid or timestamp may be "{" too, so the first
    /// "{" alone is not the message's start.
    /// </summary>
    private static string MessageJson(byte[] envelope)
    {
        var end = Array.LastIndexOf(envelope, (byte)'}');
        for (var start = Array.IndexOf(envelope, (byte)'{'); start >= 0 && start < end; start = Array.IndexOf(envelope, (byte)'{', start + 1))
        {
            var candidate = envelope.AsMemory(start, end - start + 1);
            try
            {
                using var parsed = JsonDocument.Parse(candidate);
                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return Encoding.UTF8.GetString(candidate.Span);
                }
            }
            catch (JsonException)
            {
                // A "{" inside the binary headers; the message starts later.
            }
        }

        throw new ShouldAssertException("the stored envelope holds no JSON message");
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

    /// <summary>An envelope of the service's durable inbox as the tests read it: its message's JSON.</summary>
    internal sealed record MailRequestEnvelope(Guid Id, string ReceivedAt, int Attempts, string Body);

    /// <summary>A dead letter as the tests read it: its message's JSON, and the whole row as JSON text with the body escaped.</summary>
    internal sealed record MailRequestDeadLetter(Guid Id, string ExceptionMessage, string Body, string Row);

    /// <summary>An active membership as the tests read it back: its id and its pharmacy.</summary>
    internal sealed record ActiveMembership(Guid Id, Guid PharmacyId);

    /// <summary>A membership row as the tests read it back after accepting.</summary>
    internal sealed record StoredMember(string Status, Guid? Person, string? Name, IReadOnlyList<string> TokenHashes);

    /// <summary>A membership row as the tests read it back.</summary>
    internal sealed record StoredMembership(string Status, string Role, string JobFunction, DateTimeOffset? EndedAt);
}
