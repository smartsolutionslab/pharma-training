using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The catalog through the running application: Editorial's events reach Catalog through the broker, the published
/// versions are read back by their id, and the courses are listed as their current version while it is not retired,
/// whatever order the events arrive in and however often.
/// </summary>
public class CatalogCourseVersionsTests(AspireFixture aspire)
{
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string RetiredType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionRetiredV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly string[] HiddenProperties = ["questions", "options", "answerOptions", "isCorrect", "file"];

    [Fact]
    public async Task A_published_version_is_read_back_with_its_chapters_in_order_and_listed_without_questions_or_files()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);

        await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);

        var version = await WaitUntilReadableAsync(client, published.CourseVersionId, cancellationToken);
        version.CourseId.ShouldBe(published.CourseId);
        version.VersionNumber.ShouldBe(1);
        version.Title.ShouldBe(published.Title);
        version.ValidityMonths.ShouldBe(24);
        version.IsRetired.ShouldBeFalse();
        version.Chapters.Select(chapter => chapter.Position).ShouldBe([1, 2, 3]);
        version.Chapters.Select(chapter => chapter.Kind).ShouldBe(["Text", "Video", "Pdf"]);
        version.Chapters[0].Text.ShouldBe("# Grundlagen\n\nKühlkette **immer** einhalten.");
        version.Chapters[1].VideoDurationSeconds.ShouldBe(420);
        var listed = await ListedAsync(client, published.CourseId, cancellationToken);
        listed.ShouldHaveSingleItem().ShouldBe(new ListedCourse(published.CourseId, published.CourseVersionId, 1, published.Title, 24));
        HiddenPropertiesIn(await client.GetStringAsync(VersionUri(published.CourseVersionId), cancellationToken)).ShouldBeEmpty();
        HiddenPropertiesIn(await client.GetStringAsync(CoursesUri(1), cancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_delivered_twice_keeps_what_first_arrived_and_is_listed_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);
        await WaitUntilReadableAsync(client, published.CourseVersionId, cancellationToken);

        // The same event again, under another envelope id, so the inbox lets it through to the consumer.
        var again = Guid.CreateVersion7();
        await SendAsync(PublishedType, published with { Title = $"Changed {published.Title}" }, again, cancellationToken);
        await WaitUntilHandledAsync(again, cancellationToken);

        var version = await client.GetFromJsonAsync<ReadVersion>(VersionUri(published.CourseVersionId), cancellationToken);
        version!.Title.ShouldBe(published.Title);
        (await ListedAsync(client, published.CourseId, cancellationToken)).ShouldHaveSingleItem().Title.ShouldBe(published.Title);
    }

    [Fact]
    public async Task A_retired_version_leaves_the_list_but_is_still_read_as_retired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);
        await WaitUntilReadableAsync(client, published.CourseVersionId, cancellationToken);

        var retirement = Guid.CreateVersion7();
        await SendAsync(RetiredType, Retired(published.CourseId, published.CourseVersionId), retirement, cancellationToken);
        await WaitUntilHandledAsync(retirement, cancellationToken);

        (await ListedAsync(client, published.CourseId, cancellationToken)).ShouldBeEmpty();
        var version = await client.GetFromJsonAsync<ReadVersion>(VersionUri(published.CourseVersionId), cancellationToken);
        version!.IsRetired.ShouldBeTrue();
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_makes_the_version_read_as_retired_and_never_listed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        var retirement = Guid.CreateVersion7();
        await SendAsync(RetiredType, Retired(published.CourseId, published.CourseVersionId), retirement, cancellationToken);
        await WaitUntilHandledAsync(retirement, cancellationToken);

        await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);

        var version = await WaitUntilReadableAsync(client, published.CourseVersionId, cancellationToken);
        version.IsRetired.ShouldBeTrue();
        (await ListedAsync(client, published.CourseId, cancellationToken)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_newer_version_replaces_the_older_in_the_list_whatever_order_they_arrive_in_and_the_older_stays_readable(bool newerFirst)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var courseId = Guid.CreateVersion7();
        var older = Published(courseId, Guid.CreateVersion7(), 1);
        var newer = Published(courseId, Guid.CreateVersion7(), 2);

        PublishedEvent[] arrival = newerFirst ? [newer, older] : [older, newer];

        foreach (var published in arrival)
        {
            await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);
            await WaitUntilReadableAsync(client, published.CourseVersionId, cancellationToken);
        }

        var listed = (await ListedAsync(client, courseId, cancellationToken)).ShouldHaveSingleItem();
        listed.CourseVersionId.ShouldBe(newer.CourseVersionId);
        listed.VersionNumber.ShouldBe(2);
        listed.Title.ShouldBe(newer.Title);
        using var olderRead = await client.GetAsync(VersionUri(older.CourseVersionId), cancellationToken);
        olderRead.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Retiring_the_current_version_hides_the_course_although_an_older_version_exists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");
        var courseId = Guid.CreateVersion7();
        var older = Published(courseId, Guid.CreateVersion7(), 1);
        var newer = Published(courseId, Guid.CreateVersion7(), 2);
        await SendAsync(PublishedType, older, Guid.CreateVersion7(), cancellationToken);
        await WaitUntilReadableAsync(client, older.CourseVersionId, cancellationToken);
        await SendAsync(PublishedType, newer, Guid.CreateVersion7(), cancellationToken);
        await WaitUntilReadableAsync(client, newer.CourseVersionId, cancellationToken);

        var retirement = Guid.CreateVersion7();
        await SendAsync(RetiredType, Retired(courseId, newer.CourseVersionId), retirement, cancellationToken);
        await WaitUntilHandledAsync(retirement, cancellationToken);

        (await ListedAsync(client, courseId, cancellationToken)).ShouldBeEmpty();
        using var olderRead = await client.GetAsync(VersionUri(older.CourseVersionId), cancellationToken);
        using var newerRead = await client.GetAsync(VersionUri(newer.CourseVersionId), cancellationToken);
        olderRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        newerRead.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_course_version_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("developer");

        using var response = await client.GetAsync(VersionUri(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldNotBeNullOrWhiteSpace();
        JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web)!.Code.ShouldBe("catalog.course-version-not-found");
    }

    [Fact]
    public async Task An_anonymous_caller_neither_lists_courses_nor_reads_a_course_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var listed = await client.GetAsync(CoursesUri(1), cancellationToken);
        using var read = await client.GetAsync(VersionUri(Guid.CreateVersion7()), cancellationToken);

        listed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Any_signed_in_user_without_a_realm_role_lists_courses_and_reads_a_course_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        using var visitor = await aspire.CreateHostClientAsync("visitor");
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        await SendAsync(PublishedType, published, Guid.CreateVersion7(), cancellationToken);
        await WaitUntilReadableAsync(developer, published.CourseVersionId, cancellationToken);

        using var listed = await visitor.GetAsync(CoursesUri(1), cancellationToken);
        using var read = await visitor.GetAsync(VersionUri(published.CourseVersionId), cancellationToken);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Uri VersionUri(Guid id) => new($"/catalog/course-versions/{id}", UriKind.Relative);

    private static Uri CoursesUri(int page) => new($"/catalog/courses?page={page}&pageSize=100", UriKind.Relative);

    /// <summary>A version with a text, a video and a PDF chapter, sent out of their reading order, under a title no other test uses.</summary>
    private static PublishedEvent Published(Guid courseId, Guid courseVersionId, int versionNumber) =>
        new(
            courseId,
            courseVersionId,
            versionNumber,
            $"Kühlkette {versionNumber} {Guid.CreateVersion7()}",
            24,
            [
                new SentChapter(Guid.CreateVersion7(), 3, "Checkliste", "Pdf", null, null, "blobs/checkliste.pdf"),
                new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen\n\nKühlkette **immer** einhalten.", null),
                new SentChapter(Guid.CreateVersion7(), 2, "Lagerung", "Video", 420, null, "blobs/lagerung.mp4"),
            ],
            NewMetadata());

    private static RetiredEvent Retired(Guid courseId, Guid courseVersionId) => new(courseId, courseVersionId, NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>
    /// Sends an event as Editorial's Wolverine does: to the fanout exchange named after its type, with the type and the
    /// envelope id in the message's properties and the event as camelCase JSON.
    /// </summary>
    private async Task SendAsync<T>(string type, T message, Guid messageId, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(await aspire.App.GetConnectionStringAsync("messaging", cancellationToken) ?? throw new InvalidOperationException("The broker has no connection string.")) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await WaitForExchangeAsync(connection, type, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var properties = new BasicProperties
        {
            Type = type,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
        };

        await channel.BasicPublishAsync(type, routingKey: string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>Asks for the exchange Catalog's Wolverine declares at startup, without declaring it, so the test never conflicts with its arguments.</summary>
    private static async Task WaitForExchangeAsync(IConnection connection, string exchange, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            // A passive declare of a missing exchange closes its channel, so every attempt gets a channel of its own.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
                return;
            }
            catch (OperationInterruptedException) when (TimeProvider.System.GetElapsedTime(started) < WaitTimeout)
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationInterruptedException missing)
            {
                throw new TimeoutException($"The exchange {exchange} did not exist after {WaitTimeout.TotalSeconds} s.", missing);
            }
        }
    }

    /// <summary>Barrier (a): the version is stored once Catalog answers it.</summary>
    private static async Task<ReadVersion> WaitUntilReadableAsync(HttpClient client, Guid courseVersionId, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            using var response = await client.GetAsync(VersionUri(courseVersionId), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<ReadVersion>(cancellationToken))!;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"GET {VersionUri(courseVersionId)} still answered {(int)response.StatusCode} {response.StatusCode} after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Barrier (b): Catalog's durable inbox marks the envelope handled once its consumer is done with it. A later message
    /// is no barrier, since a listener handles its messages in parallel.
    /// </summary>
    private async Task WaitUntilHandledAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("catalog-database", cancellationToken)
            ?? throw new InvalidOperationException("Catalog's database has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            var status = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (status == "Handled")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Catalog's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>Barrier (c): every entry of the course in the list, read a page at a time until the pages run out.</summary>
    private static async Task<List<ListedCourse>> ListedAsync(HttpClient client, Guid courseId, CancellationToken cancellationToken)
    {
        List<ListedCourse> found = [];
        for (var page = 1; ; page++)
        {
            var courses = await client.GetFromJsonAsync<CoursePage>(CoursesUri(page), cancellationToken);
            courses.ShouldNotBeNull();
            found.AddRange(courses.Items.Where(course => course.CourseId == courseId));
            if (courses.Items.Count == 0 || page * courses.PageSize >= courses.TotalCount)
            {
                return found;
            }
        }
    }

    /// <summary>The properties anywhere in the JSON that Catalog must never answer: questions, answers and blob references.</summary>
    private static List<string> HiddenPropertiesIn(string json)
    {
        using var document = JsonDocument.Parse(json);
        List<string> names = [];
        Collect(document.RootElement, names);
        return [.. names.Where(name => HiddenProperties.Contains(name, StringComparer.OrdinalIgnoreCase))];
    }

    private static void Collect(JsonElement element, List<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, names);
            }
        }
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record RetiredEvent(Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record ReadChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text);

    private sealed record ReadVersion(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, bool IsRetired, IReadOnlyList<ReadChapter> Chapters);

    private sealed record ListedCourse(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths);

    private sealed record CoursePage(IReadOnlyList<ListedCourse> Items, int TotalCount, int Page, int PageSize);

    private sealed record Problem(string Code);
}
