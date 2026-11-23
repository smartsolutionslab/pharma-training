using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's <c>ContentCompletedV1</c> through the running application: it reaches Assessment through the broker and
/// opens the session's one examination in Assessment's event store, once, however often it arrives, and whether or
/// not the version's test definition has arrived yet.
/// </summary>
public class AssessmentExaminationsTests(AspireFixture aspire)
{
    [Fact]
    public async Task Content_completed_opens_one_examination_for_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersionId = Guid.CreateVersion7();
        await DeliverAsync(aspire, DefinitionType, Definition(courseVersionId), cancellationToken);
        var completed = Completed(courseVersionId);

        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        var stream = (await OpenedStreamsAsync(completed.SessionId, cancellationToken)).ShouldHaveSingleItem();
        (await EventTypesAsync(stream, cancellationToken)).ShouldBe(["examination_opened"]);
    }

    [Fact]
    public async Task The_same_content_completed_delivered_twice_opens_it_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var completed = Completed(Guid.CreateVersion7());
        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        // The same event again, as a redelivery carries it: the same event identifier under another envelope id, so the inbox lets it through to the consumer.
        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        var stream = (await OpenedStreamsAsync(completed.SessionId, cancellationToken)).ShouldHaveSingleItem();
        (await EventTypesAsync(stream, cancellationToken)).ShouldBe(["examination_opened"]);
        (await CountAsync(aspire, "select count(*) from assessment.handled_content_completions where event_identifier = @id", completed.Metadata.EventIdentifier, cancellationToken))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Content_completed_before_the_test_definition_still_opens_the_examination()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersionId = Guid.CreateVersion7();
        var completed = Completed(courseVersionId);

        await DeliverAsync(aspire, CompletedType, completed, cancellationToken);

        (await CountAsync(aspire, "select count(*) from assessment.test_definitions where course_version_id = @id", courseVersionId, cancellationToken)).ShouldBe(0);
        var stream = (await OpenedStreamsAsync(completed.SessionId, cancellationToken)).ShouldHaveSingleItem();
        (await EventTypesAsync(stream, cancellationToken)).ShouldBe(["examination_opened"]);
    }

    /// <summary>The streams whose <c>ExaminationOpened</c> names the session; Marten writes the event's members as they are named.</summary>
    private async Task<List<Guid>> OpenedStreamsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select stream_id from assessment.mt_events where type = 'examination_opened' and coalesce(data->>'Session', data->>'session') = @session",
            connection);
        command.Parameters.AddWithValue("session", sessionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<Guid> streams = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            streams.Add(reader.GetGuid(0));
        }

        return streams;
    }

    private async Task<List<string>> EventTypesAsync(Guid stream, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand("select type from assessment.mt_events where stream_id = @stream order by version", connection);
        command.Parameters.AddWithValue("stream", stream);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> types = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            types.Add(reader.GetString(0));
        }

        (await CountAsync(aspire, "select count(*) from assessment.mt_streams where id = @id", stream, cancellationToken)).ShouldBe(1, $"stream {stream} is in mt_streams");
        return types;
    }
}
