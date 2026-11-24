using System.Net;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A running session across the life of its course version, with Editorial's real publish, supersede and retire
/// (README: "Begonnene Durchläufe bleiben auf der alten Version, nicht begonnene wechseln auf die neue"; Retire:
/// "laufende Durchläufe bleiben gültig"). A session already started on a version is left alone when its version is
/// superseded or retired: resuming answers the same session on its own version, and it can still be read. A retired
/// version starts no new session (#195), with 409 <c>learning.course-version-retired</c>; a version only superseded still
/// starts one, since an assignment may name it, and the newer version starts a session of its own.
/// Every learner is made a member of a pharmacy first, since only an active member may learn (#194).
/// </summary>
[Collection(EditorialDrafts.Name)]
public class LearningCourseVersionLifecycleTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_session_on_a_retired_version_resumes_unchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(learner), cancellationToken);
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);
        using var started = await StartAsync(learner, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var s1 = await BodyOfAsync(started, cancellationToken);

        using (var retired = await StepAsync(editor, v1.Id, "retire", cancellationToken))
        {
            retired.StatusCode.ShouldBe(HttpStatusCode.OK, await retired.Content.ReadAsStringAsync(cancellationToken));
        }

        // A barrier that Learning stored the retirement: a start that resumes an unfinished run is no new session, so
        // the retirement does not refuse it.
        await WaitUntilRetiredAsync(aspire, v1.Id, cancellationToken);

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
    /// v2 is drafted from v1 with <c>POST /editorial/course-versions/{id}/next</c> (#20), which copies v1's chapters,
    /// blueprint and approved questions; its publish then supersedes v1 through Editorial's real publish handler.
    /// </summary>
    [Fact]
    public async Task A_session_on_a_superseded_version_stays_on_it_and_the_new_version_starts_a_session_of_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(learner), cancellationToken);
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);
        using var started = await StartAsync(learner, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var s1 = await BodyOfAsync(started, cancellationToken);

        var v2 = await DraftNextAsync(editor, v1.Id, cancellationToken);
        await SubmitAsync(editor, v2, cancellationToken);
        await PublishAsync(editor, v2, cancellationToken);
        (await ReadAsync(editor, v1.Id, cancellationToken)).Status.ShouldBe("Superseded");
        await WaitUntilHeldAsync(aspire, v2, cancellationToken);

        using var resumed = await StartAsync(learner, v1.Id, cancellationToken);
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resumedSession = await BodyOfAsync(resumed, cancellationToken);
        resumedSession.Id.ShouldBe(s1.Id);
        resumedSession.CourseVersionId.ShouldBe(v1.Id);

        using var startedOnV2 = await StartAsync(learner, v2, cancellationToken);
        startedOnV2.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s2 = await BodyOfAsync(startedOnV2, cancellationToken);
        s2.Id.ShouldNotBe(s1.Id);
        s2.CourseVersionId.ShouldBe(v2);

        using var read = await ReadSessionAsync(learner, s1.Id, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stillOnV1 = await BodyOfAsync(read, cancellationToken);
        stillOnV1.CourseVersionId.ShouldBe(v1.Id);
        stillOnV1.Status.ShouldBe("InProgress");
    }

    [Fact]
    public async Task A_new_session_on_a_retired_version_answers_409_while_the_running_session_resumes_with_200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        using var latecomer = await aspire.CreateHostClientAsync("employee-b");
        await MemberAsync(aspire, SubjectOf(learner), cancellationToken);
        await MemberAsync(aspire, SubjectOf(latecomer), cancellationToken);
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);
        using var started = await StartAsync(learner, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var s1 = await BodyOfAsync(started, cancellationToken);

        using (var retired = await StepAsync(editor, v1.Id, "retire", cancellationToken))
        {
            retired.StatusCode.ShouldBe(HttpStatusCode.OK, await retired.Content.ReadAsStringAsync(cancellationToken));
        }

        await WaitUntilRetiredAsync(aspire, v1.Id, cancellationToken);

        using var refused = await StartAsync(latecomer, v1.Id, cancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("learning.course-version-retired");
        (await CountSessionsAsync(aspire, SubjectOf(latecomer), v1.Id, cancellationToken)).ShouldBe(0);

        using var resumed = await StartAsync(learner, v1.Id, cancellationToken);
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync(cancellationToken));
        (await BodyOfAsync(resumed, cancellationToken)).Id.ShouldBe(s1.Id);
        using var read = await ReadSessionAsync(learner, s1.Id, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyOfAsync(read, cancellationToken)).Status.ShouldBe("InProgress");
    }

    /// <summary>
    /// Learning holds no superseded state: only a retirement refuses a new session, so a version that a newer one
    /// superseded still starts one, since an assignment may name it.
    /// </summary>
    [Fact]
    public async Task A_new_session_on_a_superseded_version_still_starts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var latecomer = await aspire.CreateHostClientAsync("employee-b");
        await MemberAsync(aspire, SubjectOf(latecomer), cancellationToken);
        var v1 = await PublishedAsync(editor, cancellationToken);
        await WaitUntilHeldAsync(aspire, v1.Id, cancellationToken);

        var v2 = await DraftNextAsync(editor, v1.Id, cancellationToken);
        await SubmitAsync(editor, v2, cancellationToken);
        await PublishAsync(editor, v2, cancellationToken);
        (await ReadAsync(editor, v1.Id, cancellationToken)).Status.ShouldBe("Superseded");
        await WaitUntilHeldAsync(aspire, v2, cancellationToken);

        using var started = await StartAsync(latecomer, v1.Id, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        (await BodyOfAsync(started, cancellationToken)).CourseVersionId.ShouldBe(v1.Id);
    }
}
