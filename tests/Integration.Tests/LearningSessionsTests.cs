using System.Net;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Starting or resuming a learning session through the edge (story L1): a learner's first start creates the session
/// on the version Learning holds, every later one answers the same session until it is finished, and a session is
/// read only by its learner. Each test sends its own version's announcement and waits until Learning handled it, and
/// makes its learners members of a pharmacy first, since only an active member may learn (#194).
/// </summary>
public class LearningSessionsTests(AspireFixture aspire)
{
    [Fact]
    public async Task Starting_a_session_answers_201_with_its_location_and_every_chapter_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await LearnerAsync("employee-a", cancellationToken);

        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        started.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/learning/sessions/{session.Id}");
        session.CourseId.ShouldBe(published.CourseId);
        session.CourseVersionId.ShouldBe(published.CourseVersionId);
        session.Status.ShouldBe("InProgress");
        session.Chapters.ShouldBe(
            [.. published.Chapters
                .OrderBy(chapter => chapter.Position)
                .Select(chapter => new ChapterBody(chapter.ChapterId, chapter.Position, chapter.Kind, 0, false))]);

        using var read = await client.GetAsync(started.Headers.Location, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), session);
    }

    [Fact]
    public async Task Starting_twice_returns_the_same_session_with_200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await LearnerAsync("employee-a", cancellationToken);
        using var first = await StartAsync(client, published.CourseVersionId, cancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(cancellationToken));

        using var again = await StartAsync(client, published.CourseVersionId, cancellationToken);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyOfAsync(again, cancellationToken)).Id.ShouldBe((await BodyOfAsync(first, cancellationToken)).Id);
    }

    [Fact]
    public async Task Two_concurrent_starts_leave_one_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await LearnerAsync("employee-a", cancellationToken);

        var responses = await Task.WhenAll(
            StartAsync(client, published.CourseVersionId, cancellationToken),
            StartAsync(client, published.CourseVersionId, cancellationToken));
        try
        {
            var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ShouldHaveSingleItem();
            var createdId = (await BodyOfAsync(created, cancellationToken)).Id;
            var other = responses.Single(response => response != created);
            if (other.StatusCode == HttpStatusCode.OK)
            {
                (await BodyOfAsync(other, cancellationToken)).Id.ShouldBe(createdId);
            }
            else
            {
                other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await CodeOfAsync(other, cancellationToken)).ShouldBe("already-exists");
            }

            using var third = await StartAsync(client, published.CourseVersionId, cancellationToken);
            third.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await BodyOfAsync(third, cancellationToken)).Id.ShouldBe(createdId);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(
            "select count(*) from learning.mt_doc_learningsessionview where data->>'LearnerId' = @learner and data->>'CourseVersionId' = @version",
            connection);
        command.Parameters.AddWithValue("learner", SubjectOf(client).ToString());
        command.Parameters.AddWithValue("version", published.CourseVersionId.ToString());
        ((long)(await command.ExecuteScalarAsync(cancellationToken))!).ShouldBe(1);
    }

    [Fact]
    public async Task Another_person_starting_the_same_version_gets_a_session_of_their_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await LearnerAsync("employee-a", cancellationToken);
        using var employeeB = await LearnerAsync("employee-b", cancellationToken);

        using var startedByA = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        using var startedByB = await StartAsync(employeeB, published.CourseVersionId, cancellationToken);

        startedByA.StatusCode.ShouldBe(HttpStatusCode.Created);
        startedByB.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BodyOfAsync(startedByB, cancellationToken)).Id.ShouldNotBe((await BodyOfAsync(startedByA, cancellationToken)).Id);
    }

    [Fact]
    public async Task Reading_another_persons_session_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await LearnerAsync("employee-a", cancellationToken);
        using var employeeB = await LearnerAsync("employee-b", cancellationToken);
        using var started = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));

        using var read = await ReadSessionAsync(employeeB, (await BodyOfAsync(started, cancellationToken)).Id, cancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(read, cancellationToken)).ShouldBe("learning.session-not-found");
    }

    [Fact]
    public async Task Starting_a_version_learning_does_not_hold_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await LearnerAsync("employee-a", cancellationToken);

        using var unknown = await StartAsync(client, Guid.CreateVersion7(), cancellationToken);
        using var empty = await StartAsync(client, Guid.Empty, cancellationToken);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknown, cancellationToken)).ShouldBe("learning.course-version-not-found");
        empty.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(empty, cancellationToken)).ShouldBe("learning.course-version-not-found");
    }

    [Fact]
    public async Task Without_a_token_starting_and_reading_answer_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var started = await StartAsync(client, Guid.CreateVersion7(), cancellationToken);
        using var read = await ReadSessionAsync(client, Guid.CreateVersion7(), cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>The user's client, once Learning holds a membership of theirs.</summary>
    private async Task<HttpClient> LearnerAsync(string user, CancellationToken cancellationToken)
    {
        var client = await aspire.CreateHostClientAsync(user);
        try
        {
            await MemberAsync(aspire, SubjectOf(client), cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<PublishedEvent> PublishedToLearningAsync(CancellationToken cancellationToken)
    {
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);
        return published;
    }
}
