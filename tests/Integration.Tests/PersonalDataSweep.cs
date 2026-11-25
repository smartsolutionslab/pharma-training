using Aspire.Hosting.Testing;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Searches every text, varchar and jsonb column, and every bytea column (Wolverine's envelope bodies), of the given
/// schemas of one service's database for a piece of personal data (#115), and answers the <c>schema.table.column</c>
/// of each that holds it. Each erasure slice sweeps its service's database with it.
/// </summary>
internal sealed class PersonalDataSweep(AspireFixture aspire, string databaseResource, params string[] schemas)
{
    public async Task<List<string>> ColumnsHoldingAsync(string needle, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync(databaseResource, cancellationToken)
            ?? throw new InvalidOperationException($"The resource {databaseResource} has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        List<(string Schema, string Table, string Column, string Type)> columns = [];
        await using (var listing = new NpgsqlCommand(
            """
            SELECT c.table_schema, c.table_name, c.column_name, c.data_type
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = ANY(@schemas)
              AND t.table_type = 'BASE TABLE'
              AND c.data_type IN ('text', 'character varying', 'jsonb', 'bytea')
            ORDER BY 1, 2, 3
            """,
            connection))
        {
            listing.Parameters.AddWithValue("schemas", schemas);
            await using var reader = await listing.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        columns.ShouldNotBeEmpty($"{databaseResource} has no text columns in {string.Join(", ", schemas)}: is the schema there?");

        List<string> holding = [];
        foreach (var (schema, table, column, type) in columns)
        {
            var quoted = $"\"{schema}\".\"{table}\"";
            var condition = type == "bytea"
                ? $"position(convert_to(@needle, 'UTF8') in \"{column}\") > 0"
                : $"strpos(\"{column}\"::text, @needle) > 0";
            await using var search = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {quoted} WHERE {condition})", connection);
            search.Parameters.AddWithValue("needle", needle);
            if (await search.ExecuteScalarAsync(cancellationToken) is true)
            {
                holding.Add($"{schema}.{table}.{column}");
            }
        }

        return holding;
    }
}
