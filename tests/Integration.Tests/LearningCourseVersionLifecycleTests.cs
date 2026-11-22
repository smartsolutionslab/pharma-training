using System.Net;
using Aspire.Hosting.Testing;
using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A running session across the life of its course version, with Editorial's real publish, supersede and retire
/// (README: "Begonnene Durchläufe bleiben auf der alten Version, nicht begonnene wechseln auf die neue"; Retire:
/// "laufende Durchläufe bleiben gültig"). Learning changes nothing when its version is superseded or retired: resuming
/// answers the same session on its own version, and the newer version starts a session of its own.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class LearningCourseVersionLifecycleTests(AspireFixture aspire)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task A_session_on_a_retired_version_resumes_unchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);
        using var started = await StartAsync(learner, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var s1 = await BodyOfAsync(started, cancellationToken);

        using (var retired = await StepAsync(editor, v1.Id, "retire", cancellationToken))
        {
            retired.StatusCode.ShouldBe(HttpStatusCode.OK, await retired.Content.ReadAsStringAsync(cancellationToken));
        }

        // A barrier that CourseVersionRetiredV1 went out and was delivered; Learning does not consume it.
        await WaitUntilCatalogHoldsRetirementAsync(v1.Id, cancellationToken);

        using var resumed = await StartAsync(learner, v1.Id, cancellationToken);
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resumedSession = await BodyOfAsync(resumed, cancellationToken);
        resumedSession.Id.ShouldBe(s1.Id);
        resumedSession.CourseVersionId.ShouldBe(v1.Id);
        resumedSession.Status.ShouldBe("InProgress");
        ShouldDescribeTheSame(resumedSession, s1);
        using var read = await ReadSessionAsync(learner, s1.Id, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), s1);
    }

    /// <summary>
    /// v2 is made the next version of v1's course with <see cref="SeedAsNextVersionAsync"/>, as
    /// <c>EditorialWorkflowTests</c> does, until #20 (<c>POST /editorial/course-versions/{id}/next</c>) is on main; its
    /// publish then supersedes v1 through Editorial's real publish handler.
    /// </summary>
    [Fact]
    public async Task A_session_on_a_superseded_version_stays_on_it_and_the_new_version_starts_a_session_of_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);
        using var started = await StartAsync(learner, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var s1 = await BodyOfAsync(started, cancellationToken);

        var v2 = await ReadyToSubmitAsync(editor, cancellationToken);
        await SubmitAsync(editor, v2.Id, cancellationToken);
        await SeedAsNextVersionAsync(await ConnectionStringAsync(aspire, cancellationToken), v2.Id, v1.CourseId, 2, cancellationToken);
        await ApproveAllAsync(editor, v2.Id, cancellationToken);
        await PublishAsync(editor, v2.Id, cancellationToken);
        (await ReadAsync(editor, v1.Id, cancellationToken)).Status.ShouldBe("Superseded");
        await WaitUntilHeldAsync(aspire, v2.Id, cancellationToken);

        using var resumed = await StartAsync(learner, v1.Id, cancellationToken);
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resumedSession = await BodyOfAsync(resumed, cancellationToken);
        resumedSession.Id.ShouldBe(s1.Id);
        resumedSession.CourseVersionId.ShouldBe(v1.Id);

        using var startedOnV2 = await StartAsync(learner, v2.Id, cancellationToken);
        startedOnV2.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s2 = await BodyOfAsync(startedOnV2, cancellationToken);
        s2.Id.ShouldNotBe(s1.Id);
        s2.CourseVersionId.ShouldBe(v2.Id);

        using var read = await ReadSessionAsync(learner, s1.Id, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stillOnV1 = await BodyOfAsync(read, cancellationToken);
        stillOnV1.CourseVersionId.ShouldBe(v1.Id);
        stillOnV1.Status.ShouldBe("InProgress");
    }

    /// <summary>Polls Catalog's database until it holds the version's retirement, which only Editorial's <c>CourseVersionRetiredV1</c> puts there.</summary>
    private async Task WaitUntilCatalogHoldsRetirementAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("catalog-database", cancellationToken)
            ?? throw new InvalidOperationException("Catalog's database has no connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select count(*) from catalog.course_version_retirements where course_version_id = @id", connection);
            command.Parameters.AddWithValue("id", versionId);
            if ((long)(await command.ExecuteScalarAsync(cancellationToken))! > 0)
            {
                return;
            }

            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(WaitTimeout, $"Catalog never heard that version {versionId} was retired.");
            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
