using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// The sweep's purge of erased persons' messages (#439) names the module's schema in its SQL, so it refuses any schema
/// that is not a plain lowercase identifier before it opens a connection, and with no message types it has nothing to
/// delete and opens none. The data source points at a host that does not resolve: opening it would fail the test.
/// </summary>
public sealed class PersonalMessagePurgeGuardTests : IAsyncDisposable
{
    private static readonly IReadOnlyCollection<Type> Types = [typeof(AnnouncedV1)];

    private readonly NpgsqlDataSource dataSource = NpgsqlDataSource.Create("Host=unused.invalid;Timeout=1");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("Learning")]
    [InlineData("learning; drop table x")]
    [InlineData("\"learning\"")]
    public async Task A_schema_that_is_no_plain_lowercase_identifier_is_refused_before_anything_is_deleted(string schema)
    {
        var purge = new PersonalMessagePurge(dataSource);

        await Should.ThrowAsync<GuardException>(() => purge.PurgeErasedAsync(schema, Types, Token));
    }

    [Fact]
    public async Task No_message_types_delete_nothing_and_open_no_connection()
    {
        var purge = new PersonalMessagePurge(dataSource);

        var removed = await purge.PurgeErasedAsync("learning", [], Token);

        removed.ShouldBe(0);
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
