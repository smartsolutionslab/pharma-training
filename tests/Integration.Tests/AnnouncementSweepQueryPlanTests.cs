using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pass of a module's announcement sweep (#277) runs every interval on every replica and normally finds nothing, so a
/// LIMIT never cuts its query short: without an index on the stored time of its terminal events, every pass reads the
/// whole event table, which grows with every progress report (review of #322, finding 1). The plan of the exact query the
/// sweep runs (<see cref="UnannouncedEvents.Query"/>) must read the terminal events of the window through an index on
/// their time. The test tables hold a few rows, on which a sequential scan is always cheapest, so the plan is asked with
/// sequential scans switched off: the question is whether an index can answer the query at all, not which the planner
/// prefers on a nearly empty table.
/// </summary>
public class AnnouncementSweepQueryPlanTests(AspireFixture aspire)
{
    private static readonly string[] IndexScans = ["Index Scan", "Index Only Scan", "Bitmap Index Scan"];

    [Theory]
    [InlineData("learning-database", "learning", "content_completed")]
    [InlineData("assessment-database", "assessment", "examination_passed")]
    [InlineData("assessment-database", "assessment", "attempt_submitted")]
    [InlineData("assessment-database", "assessment", "examination_exhausted")]
    public async Task The_sweeps_query_reads_the_window_of_its_terminal_events_through_an_index_never_the_whole_event_table(
        string database,
        string schema,
        string storedType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var nodes = await PlanAsync(database, UnannouncedEvents.Query(schema, storedType), cancellationToken);

        nodes.ShouldNotContain(
            node => Text(node, "Node Type") == "Seq Scan" && Text(node, "Relation Name") == "mt_events",
            $"The plan reads all of {schema}.mt_events: {string.Join(" > ", nodes.Select(Describe))}");
        nodes.ShouldContain(
            node => IndexScans.Contains(Text(node, "Node Type")) && Text(node, "Index Cond").Contains("timestamp", StringComparison.Ordinal),
            $"No index scan on the stored time of {storedType}: {string.Join(" > ", nodes.Select(Describe))}");
    }

    private static string Text(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string Describe(JsonElement node) =>
        $"{Text(node, "Node Type")} {Text(node, "Relation Name")}{Text(node, "Index Name")} [{Text(node, "Index Cond")}]";

    private static void Collect(JsonElement node, List<JsonElement> nodes)
    {
        nodes.Add(node);
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, nodes);
            }
        }
    }

    private async Task<List<JsonElement>> PlanAsync(string database, string query, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync(database, cancellationToken)
            ?? throw new InvalidOperationException($"{database} has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var switchOff = new NpgsqlCommand("set local enable_seqscan = off", connection, transaction))
        {
            await switchOff.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = TimeProvider.System.GetUtcNow();
        await using var explain = new NpgsqlCommand($"explain (format json) {query}", connection, transaction);
        explain.Parameters.AddWithValue("from", now.AddDays(-7));
        explain.Parameters.AddWithValue("until", now.AddMinutes(-2));
        explain.Parameters.AddWithValue("after", UnannouncedEvent.BeforeFirst);
        explain.Parameters.AddWithValue("limit", 100);
        var plan = (string)(await explain.ExecuteScalarAsync(cancellationToken))!;
        await transaction.RollbackAsync(cancellationToken);

        using var document = JsonDocument.Parse(plan);
        List<JsonElement> nodes = [];
        Collect(document.RootElement[0].GetProperty("Plan").Clone(), nodes);
        return nodes;
    }
}
