using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The people Editorial names beside a question's approval and a version's return (#115), planted and read in its own
/// database. A test makes a draft, submits it and returns it through the API, then plants a person of its own as the
/// question's approver and the return's returner, since a shared local user must never be erased.
/// </summary>
internal static class EditorialReviewers
{
    /// <summary>Makes <paramref name="person"/>, named <paramref name="name"/>, the approver of the question and the returner of every return of the version.</summary>
    public static async Task AttributeAsync(string connectionString, Guid version, Guid question, Guid person, string name, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var approved = new NpgsqlCommand(
            "UPDATE editorial.course_version_questions SET reviewed_by = @person, reviewed_by_name = @name, reviewed_at = now() WHERE id = @id",
            connection))
        {
            approved.Parameters.AddWithValue("person", person);
            approved.Parameters.AddWithValue("name", name);
            approved.Parameters.AddWithValue("id", question);
            (await approved.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"Editorial stores no question {question}.");
        }

        await using var returned = new NpgsqlCommand(
            "UPDATE editorial.course_version_returns SET returned_by = @person, returned_by_name = @name WHERE course_version_id = @id",
            connection);
        returned.Parameters.AddWithValue("person", person);
        returned.Parameters.AddWithValue("name", name);
        returned.Parameters.AddWithValue("id", version);
        (await returned.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"Editorial stores not exactly one return of version {version}.");
    }

    /// <summary>The question's approver and the name kept beside them.</summary>
    public static async Task<(Guid? By, string? Name)> ApprovalOfAsync(string connectionString, Guid question, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT reviewed_by, reviewed_by_name FROM editorial.course_version_questions WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", question);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"Editorial stores no question {question}.");
        return (
            await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetGuid(0),
            await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1));
    }

    /// <summary>The version's one return: who returned it, the name kept beside them, and the reason.</summary>
    public static async Task<(Guid By, string? Name, string Reason)> ReturnOfAsync(string connectionString, Guid version, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT returned_by, returned_by_name, reason FROM editorial.course_version_returns WHERE course_version_id = @id", connection);
        command.Parameters.AddWithValue("id", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue($"Editorial stores no return of version {version}.");
        var found = (reader.GetGuid(0), await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1), reader.GetString(2));
        (await reader.ReadAsync(cancellationToken)).ShouldBeFalse($"Editorial stores more than one return of version {version}.");
        return found;
    }

    /// <summary>Makes <paramref name="person"/>, named <paramref name="name"/>, the approver of the question, as an approval with their token would.</summary>
    public static async Task AttributeApprovalAsync(string connectionString, Guid question, Guid person, string name, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var approved = new NpgsqlCommand(
            "UPDATE editorial.course_version_questions SET reviewed_by = @person, reviewed_by_name = @name, reviewed_at = now() WHERE id = @id",
            connection);
        approved.Parameters.AddWithValue("person", person);
        approved.Parameters.AddWithValue("name", name);
        approved.Parameters.AddWithValue("id", question);
        (await approved.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1, $"Editorial stores no question {question}.");
    }

    /// <summary>Each question's approver of the version and the name kept beside them.</summary>
    public static async Task<List<(Guid? By, string? Name)>> ApprovalsOfVersionAsync(string connectionString, Guid version, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT reviewed_by, reviewed_by_name FROM editorial.course_version_questions WHERE course_version_id = @id ORDER BY position", connection);
        command.Parameters.AddWithValue("id", version);
        List<(Guid? By, string? Name)> approvals = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            approvals.Add((
                await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetGuid(0),
                await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1)));
        }

        return approvals;
    }

    /// <summary>The tombstones Editorial keeps of the person (#115): one once they are erased.</summary>
    public static async Task<long> TombstonesOfAsync(string connectionString, Guid person, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM editorial.erased_persons WHERE person_id = @id", connection);
        command.Parameters.AddWithValue("id", person);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
