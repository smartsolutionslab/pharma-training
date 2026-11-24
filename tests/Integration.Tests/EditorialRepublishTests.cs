using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RabbitMQ.Client;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Republishing Editorial's course-version events (#189): a queue bound after a version went out receives, once an
/// editor republishes, the version, its test definition and its retirement, with the payload they were first announced
/// with and under new event identifiers; the consumers that already hold them keep one copy however often it runs; and
/// one run goes at a time.
/// The database is shared with the other tests, so every queue is read for the test's own versions only.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialRepublishTests(AspireFixture aspire)
{
    private const string CourseVersionPublishedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string TestDefinitionPublishedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.TestDefinitionPublishedV1";
    private const string CourseVersionRetiredExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionRetiredV1";

    private const string ComplianceVersionsById = "select count(*) from compliance.course_versions where id = @id";
    private const string ComplianceRetirementsById = "select count(*) from compliance.course_version_retirements where id = @id";
    private const string LearningVersionsById = "select count(*) from learning.course_versions where id = @id";
    private const string CatalogVersionsById = "select count(*) from catalog.course_versions where id = @id";
    private const string CatalogRetirementsById = "select count(*) from catalog.course_version_retirements where course_version_id = @id";
    private const string DeadLettersById = "select count(*) from wolverine.wolverine_dead_letters where id = @id";

    private static readonly Uri Republish = new("/editorial/course-versions/republish", UriKind.Relative);

    /// <summary>
    /// Version 1 is published, superseded by version 2 and retired before any queue of the test's is bound, so only the
    /// republish can bring them. There is no order asserted across queues: each event type has an exchange of its own.
    /// </summary>
    [Fact]
    public async Task A_queue_bound_after_a_version_was_published_and_retired_receives_the_version_its_test_definition_and_the_retirement_once_republished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var first = await PublishedAsync(client, cancellationToken);
        var second = await SupersedeAsync(client, first.Id, cancellationToken);
        await RetireAsync(client, first.Id, cancellationToken);

        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionPublishedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, TestDefinitionPublishedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionRetiredExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var courseQueue = await Broker.BindQueueAsync(channel, CourseVersionPublishedExchange, cancellationToken);
        var testQueue = await Broker.BindQueueAsync(channel, TestDefinitionPublishedExchange, cancellationToken);
        var retiredQueue = await Broker.BindQueueAsync(channel, CourseVersionRetiredExchange, cancellationToken);

        var counts = await RepublishAsync(client, cancellationToken);

        counts.CourseVersions.ShouldBeGreaterThanOrEqualTo(2);
        counts.TestDefinitions.ShouldBeGreaterThanOrEqualTo(2);
        counts.Retirements.ShouldBeGreaterThanOrEqualTo(1);

        var courses = await ReceiveBothAsync(channel, courseQueue, first.Id, second.Id, cancellationToken);
        var firstCourse = courses.Where(received => VersionOf(received) == first.Id).ShouldHaveSingleItem();
        firstCourse.GetProperty("courseId").GetGuid().ShouldBe(first.CourseId);
        firstCourse.GetProperty("versionNumber").GetInt32().ShouldBe(1);
        ChapterIdsOf(firstCourse).ShouldBe([first.Chapter]);
        var secondCourse = courses.Where(received => VersionOf(received) == second.Id).ShouldHaveSingleItem();
        secondCourse.GetProperty("courseId").GetGuid().ShouldBe(first.CourseId);
        secondCourse.GetProperty("versionNumber").GetInt32().ShouldBe(2);
        ChapterIdsOf(secondCourse).ShouldBe(second.Chapters);

        var tests = await ReceiveBothAsync(channel, testQueue, first.Id, second.Id, cancellationToken);
        var firstTest = tests.Where(received => VersionOf(received) == first.Id).ShouldHaveSingleItem();
        firstTest.GetProperty("courseId").GetGuid().ShouldBe(first.CourseId);
        QuestionIdsOf(firstTest).ShouldBe([first.Question]);
        var secondTest = tests.Where(received => VersionOf(received) == second.Id).ShouldHaveSingleItem();
        secondTest.GetProperty("courseId").GetGuid().ShouldBe(first.CourseId);
        QuestionIdsOf(secondTest).ShouldBe(second.Questions);

        // A version retired after the republish is a marker: its retirement reaches the queue after every republished one.
        var marker = await PublishedAsync(client, cancellationToken);
        await RetireAsync(client, marker.Id, cancellationToken);
        var retirements = await Broker.ReceiveUntilAsync<JsonElement>(channel, retiredQueue, received => VersionOf(received) == marker.Id, cancellationToken);
        retirements.Where(received => VersionOf(received) == first.Id).ShouldHaveSingleItem().GetProperty("courseId").GetGuid().ShouldBe(first.CourseId);
        retirements.Where(received => VersionOf(received) == second.Id).ShouldBeEmpty();
    }

    /// <summary>
    /// One pair of queues is bound before the version is published and catches the originals; the second pair is bound
    /// once the originals are routed, so it catches only what the republish sends.
    /// </summary>
    [Fact]
    public async Task The_republished_events_carry_what_was_published_under_new_event_identifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // A version published first, so Editorial has sent a test definition and Wolverine has declared its exchange.
        await PublishedAsync(client, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionPublishedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, TestDefinitionPublishedExchange, cancellationToken);
        var originalCourseQueue = await Broker.BindQueueAsync(channel, CourseVersionPublishedExchange, cancellationToken);
        var originalTestQueue = await Broker.BindQueueAsync(channel, TestDefinitionPublishedExchange, cancellationToken);

        var published = await PublishedAsync(client, cancellationToken);
        var originalCourse = await Broker.ReceiveAsync<JsonElement>(channel, originalCourseQueue, received => VersionOf(received) == published.Id, cancellationToken);
        var originalTest = await Broker.ReceiveAsync<JsonElement>(channel, originalTestQueue, received => VersionOf(received) == published.Id, cancellationToken);
        var republishedCourseQueue = await Broker.BindQueueAsync(channel, CourseVersionPublishedExchange, cancellationToken);
        var republishedTestQueue = await Broker.BindQueueAsync(channel, TestDefinitionPublishedExchange, cancellationToken);

        await RepublishAsync(client, cancellationToken);

        var republishedCourse = await Broker.ReceiveAsync<JsonElement>(channel, republishedCourseQueue, received => VersionOf(received) == published.Id, cancellationToken);
        var republishedTest = await Broker.ReceiveAsync<JsonElement>(channel, republishedTestQueue, received => VersionOf(received) == published.Id, cancellationToken);
        ShouldCarryTheSamePayloadUnderANewIdentifier(originalCourse, republishedCourse);
        ShouldCarryTheSamePayloadUnderANewIdentifier(originalTest, republishedTest);
    }

    /// <summary>
    /// The second run's events are read off queues of the test's own under the message-id Wolverine gave them, which is
    /// the envelope id each consumer's inbox stores, so the test waits until Compliance, Learning and Catalog handled
    /// exactly them.
    /// </summary>
    [Fact]
    public async Task Republishing_twice_leaves_one_copy_of_the_version_and_one_retirement_in_compliance_learning_and_catalog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var published = await PublishedAsync(client, cancellationToken);
        await RetireAsync(client, published.Id, cancellationToken);

        await RepublishAsync(client, cancellationToken);
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionPublishedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionRetiredExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var courseQueue = await Broker.BindQueueAsync(channel, CourseVersionPublishedExchange, cancellationToken);
        var retiredQueue = await Broker.BindQueueAsync(channel, CourseVersionRetiredExchange, cancellationToken);
        await RepublishAsync(client, cancellationToken);

        var (courseMessage, _) = await Broker.ReceiveWithMessageIdAsync(channel, courseQueue, received => VersionOf(received) == published.Id, cancellationToken);
        var (retiredMessage, _) = await Broker.ReceiveWithMessageIdAsync(channel, retiredQueue, received => VersionOf(received) == published.Id, cancellationToken);
        await ComplianceDatabase.WaitUntilHandledAsync(aspire, courseMessage, cancellationToken);
        await ComplianceDatabase.WaitUntilHandledAsync(aspire, retiredMessage, cancellationToken);
        await LearningDatabase.WaitUntilHandledAsync(aspire, courseMessage, cancellationToken);
        await CatalogDatabase.WaitUntilHandledAsync(aspire, courseMessage, cancellationToken);
        await CatalogDatabase.WaitUntilHandledAsync(aspire, retiredMessage, cancellationToken);

        (await ComplianceDatabase.CountAsync(aspire, ComplianceVersionsById, published.Id, cancellationToken)).ShouldBe(1);
        (await ComplianceDatabase.CountAsync(aspire, ComplianceRetirementsById, published.Id, cancellationToken)).ShouldBe(1);
        (await LearningDatabase.CountAsync(aspire, LearningVersionsById, published.Id, cancellationToken)).ShouldBe(1);
        (await CatalogDatabase.CountAsync(aspire, CatalogVersionsById, published.Id, cancellationToken)).ShouldBe(1);
        (await CatalogDatabase.CountAsync(aspire, CatalogRetirementsById, published.Id, cancellationToken)).ShouldBe(1);
        (await ComplianceDatabase.CountAsync(aspire, DeadLettersById, courseMessage, cancellationToken)).ShouldBe(0);
        (await ComplianceDatabase.CountAsync(aspire, DeadLettersById, retiredMessage, cancellationToken)).ShouldBe(0);
        (await LearningDatabase.CountAsync(aspire, DeadLettersById, courseMessage, cancellationToken)).ShouldBe(0);
        (await CatalogDatabase.CountAsync(aspire, DeadLettersById, courseMessage, cancellationToken)).ShouldBe(0);
        (await CatalogDatabase.CountAsync(aspire, DeadLettersById, retiredMessage, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// One run at a time (ADR 0019): while the test holds the run's advisory lock as another run would, a republish is
    /// refused as running at once; once the lock is released, the next run goes through.
    /// </summary>
    [Fact]
    public async Task A_republish_while_another_run_holds_the_lock_answers_409_republish_running()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);

        await using (await EditorialDatabase.HoldRepublishLockAsync(connectionString, cancellationToken))
        {
            using var refused = await client.PostAsync(Republish, null, cancellationToken);
            var body = await refused.Content.ReadAsStringAsync(cancellationToken);

            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"POST {Republish} answered {(int)refused.StatusCode} {body}");
            using var problem = JsonDocument.Parse(body);
            problem.RootElement.GetProperty("code").GetString().ShouldBe("editorial.republish-running");
            problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf("editorial.republish-running"));
        }

        await RepublishAsync(client, cancellationToken);
    }

    private static async Task<RepublishedCounts> RepublishAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(Republish, null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {Republish} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        var counts = await response.Content.ReadFromJsonAsync<RepublishedCounts>(cancellationToken);
        counts.ShouldNotBeNull();
        return counts;
    }

    private static async Task RetireAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var retired = await StepAsync(client, id, "retire", cancellationToken);
        retired.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST /editorial/course-versions/{id}/retire answered {(int)retired.StatusCode} {await retired.Content.ReadAsStringAsync(cancellationToken)}");
    }

    /// <summary>
    /// Drafts, changes and publishes the next version of a published one, which supersedes it. Its chapter is rewritten,
    /// which sends its question back for approval, so the editor approves it again before publishing.
    /// </summary>
    private static async Task<NextVersion> SupersedeAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var nextId = await DraftNextAsync(client, id, cancellationToken);
        var drafted = await ContentAsync(client, nextId, cancellationToken);
        var chapter = new Uri($"/editorial/course-versions/{nextId}/chapters/{drafted.Chapters.Single()}", UriKind.Relative);
        using (var replaced = await client.PutAsJsonAsync(chapter, new { title = "Händehygiene", text = "Neu: vor und nach …" }, cancellationToken))
        {
            replaced.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {chapter} answered {(int)replaced.StatusCode}.");
        }

        await SubmitAsync(client, nextId, cancellationToken);
        await ApproveAllAsync(client, nextId, cancellationToken);
        await PublishAsync(client, nextId, cancellationToken);
        return await ContentAsync(client, nextId, cancellationToken);
    }

    /// <summary>The ids of the version's chapters and questions, in their order, as the edge answers them.</summary>
    private static async Task<NextVersion> ContentAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var json = await client.GetStringAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        using var document = JsonDocument.Parse(json);
        return new NextVersion(
            id,
            [.. document.RootElement.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("id").GetGuid())],
            [.. document.RootElement.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("id").GetGuid())]);
    }

    /// <summary>Every message on the queue up to the one that completes both versions, whichever of the two comes first.</summary>
    private static Task<IReadOnlyList<JsonElement>> ReceiveBothAsync(IChannel channel, string queue, Guid first, Guid second, CancellationToken cancellationToken)
    {
        HashSet<Guid> seen = [];
        return Broker.ReceiveUntilAsync<JsonElement>(
            channel,
            queue,
            received =>
            {
                seen.Add(VersionOf(received));
                return seen.Contains(first) && seen.Contains(second);
            },
            cancellationToken);
    }

    private static void ShouldCarryTheSamePayloadUnderANewIdentifier(JsonElement original, JsonElement republished)
    {
        EventIdentifierOf(republished).ShouldNotBe(EventIdentifierOf(original));
        var originalPayload = WithoutMetadata(original);
        var republishedPayload = WithoutMetadata(republished);
        JsonNode.DeepEquals(originalPayload, republishedPayload).ShouldBeTrue($"original:    {originalPayload.ToJsonString()}\nrepublished: {republishedPayload.ToJsonString()}");
    }

    private static JsonObject WithoutMetadata(JsonElement received)
    {
        var payload = JsonNode.Parse(received.GetRawText())?.AsObject() ?? throw new InvalidOperationException($"Not a JSON object: {received.GetRawText()}");
        payload.Remove("metadata").ShouldBeTrue(received.GetRawText());
        return payload;
    }

    private static Guid EventIdentifierOf(JsonElement received) => received.GetProperty("metadata").GetProperty("eventIdentifier").GetGuid();

    private static Guid VersionOf(JsonElement received) => received.GetProperty("courseVersionId").GetGuid();

    private static List<Guid> ChapterIdsOf(JsonElement received) =>
        [.. received.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("chapterId").GetGuid())];

    private static List<Guid> QuestionIdsOf(JsonElement received) =>
        [.. received.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("questionId").GetGuid())];

    /// <summary>The answer as the test reads it; the module's DTO is not referenced.</summary>
    private sealed record RepublishedCounts(int CourseVersions, int TestDefinitions, int Retirements);

    private sealed record NextVersion(Guid Id, IReadOnlyList<Guid> Chapters, IReadOnlyList<Guid> Questions);
}
