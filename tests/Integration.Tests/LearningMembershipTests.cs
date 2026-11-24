using System.Net;
using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Only an active member of a pharmacy may start or resume a learning session (#194), or report progress on one (#242),
/// whichever pharmacy it is, by
/// Learning's own copy of the memberships. Someone who is no member is refused with <c>learning.membership-missing</c>
/// before Learning says whether a version or a session exists; a former member still reads their own session.
/// <c>visitor</c> is never a member anywhere; <c>former-employee</c> is made one here alone, and every test that does
/// so ends that membership before it asserts the refusal, so she is never an active member when another test runs.
/// The tests of a class run one after another.
/// </summary>
public class LearningMembershipTests(AspireFixture aspire)
{
    private const string MembershipMissing = "learning.membership-missing";

    [Fact]
    public async Task A_member_starts_and_reads_a_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var member = await aspire.CreateHostClientAsync("employee-b");
        await MemberAsync(aspire, SubjectOf(member), cancellationToken);

        using var started = await StartAsync(member, published.CourseVersionId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        using var read = await ReadSessionAsync(member, session.Id, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), session);
    }

    [Fact]
    public async Task A_signed_in_person_without_a_membership_may_not_start_a_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var started = await StartAsync(visitor, published.CourseVersionId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await started.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(started, cancellationToken)).ShouldBe(MembershipMissing);
        (await SessionsOfAsync(SubjectOf(visitor), published.CourseVersionId, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_non_member_starting_a_version_learning_does_not_hold_is_refused_as_a_non_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var started = await StartAsync(visitor, Guid.CreateVersion7(), cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await started.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(started, cancellationToken)).ShouldBe(MembershipMissing);
    }

    [Fact]
    public async Task A_person_whose_membership_ended_may_not_start_or_resume_but_still_reads_their_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versionA = await PublishedToLearningAsync(cancellationToken);
        var versionB = await PublishedToLearningAsync(cancellationToken);
        using var former = await aspire.CreateHostClientAsync("former-employee");
        var person = SubjectOf(former);
        var pharmacy = Guid.CreateVersion7();
        var membership = await MemberAsync(aspire, person, pharmacy, cancellationToken);
        SessionBody session;
        try
        {
            using var started = await StartAsync(former, versionA.CourseVersionId, cancellationToken);
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
            session = await BodyOfAsync(started, cancellationToken);
        }
        finally
        {
            await EndAsync(aspire, membership, person, pharmacy, cancellationToken);
        }

        using var startedOnB = await StartAsync(former, versionB.CourseVersionId, cancellationToken);
        using var resumed = await StartAsync(former, versionA.CourseVersionId, cancellationToken);
        using var read = await ReadSessionAsync(former, session.Id, cancellationToken);

        startedOnB.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await startedOnB.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(startedOnB, cancellationToken)).ShouldBe(MembershipMissing);
        resumed.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await resumed.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(resumed, cancellationToken)).ShouldBe(MembershipMissing);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), session);
    }

    [Fact]
    public async Task A_signed_in_person_without_a_membership_may_not_read_any_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(employeeA), cancellationToken);
        using var started = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var another = await ReadSessionAsync(visitor, (await BodyOfAsync(started, cancellationToken)).Id, cancellationToken);
        using var unknown = await ReadSessionAsync(visitor, Guid.CreateVersion7(), cancellationToken);

        another.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await another.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(another, cancellationToken)).ShouldBe(MembershipMissing);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await unknown.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(unknown, cancellationToken)).ShouldBe(MembershipMissing);
    }

    [Fact]
    public async Task A_member_reading_another_persons_session_is_still_told_it_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        using var employeeB = await aspire.CreateHostClientAsync("employee-b");
        await MemberAsync(aspire, SubjectOf(employeeA), cancellationToken);
        await MemberAsync(aspire, SubjectOf(employeeB), cancellationToken);
        using var started = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));

        using var read = await ReadSessionAsync(employeeB, (await BodyOfAsync(started, cancellationToken)).Id, cancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound, await read.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(read, cancellationToken)).ShouldBe("learning.session-not-found");
    }

    [Fact]
    public async Task A_member_reports_progress_on_their_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var member = await aspire.CreateHostClientAsync("employee-b");
        await MemberAsync(aspire, SubjectOf(member), cancellationToken);
        using var started = await StartAsync(member, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        // A text chapter, which is not paced (ADR 0024), so the report does not wait for a video's playback time.
        var text = ChapterOf(published, "Text");

        using var reported = await ReportProgressAsync(member, session.Id, text, 100, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(cancellationToken));
        (await BodyOfAsync(reported, cancellationToken)).Chapters.Single(chapter => chapter.ChapterId == text).ProgressPercent.ShouldBe(100);
    }

    [Fact]
    public async Task A_person_whose_membership_ended_may_not_report_progress_on_their_own_session_but_still_reads_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var former = await aspire.CreateHostClientAsync("former-employee");
        var person = SubjectOf(former);
        var pharmacy = Guid.CreateVersion7();
        var membership = await MemberAsync(aspire, person, pharmacy, cancellationToken);
        SessionBody session;
        try
        {
            using var started = await StartAsync(former, published.CourseVersionId, cancellationToken);
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
            session = await BodyOfAsync(started, cancellationToken);
        }
        finally
        {
            await EndAsync(aspire, membership, person, pharmacy, cancellationToken);
        }

        var stored = await EventsInAsync(session.Id, cancellationToken);

        using var reported = await ReportProgressAsync(former, session.Id, ChapterOf(published, "Video"), 60, cancellationToken);
        using var read = await ReadSessionAsync(former, session.Id, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await reported.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(reported, cancellationToken)).ShouldBe(MembershipMissing);
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), session);
    }

    [Fact]
    public async Task A_signed_in_person_without_a_membership_may_not_report_progress_on_any_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(employeeA), cancellationToken);
        using var started = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        var stored = await EventsInAsync(session.Id, cancellationToken);
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var another = await ReportProgressAsync(visitor, session.Id, ChapterOf(published, "Video"), 60, cancellationToken);
        using var unknown = await ReportProgressAsync(visitor, Guid.CreateVersion7(), ChapterOf(published, "Video"), 60, cancellationToken);

        another.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await another.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(another, cancellationToken)).ShouldBe(MembershipMissing);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await unknown.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(unknown, cancellationToken)).ShouldBe(MembershipMissing);
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
    }

    [Fact]
    public async Task Without_a_token_reporting_progress_answers_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = aspire.CreateHostClient();

        using var reported = await ReportProgressAsync(anonymous, Guid.CreateVersion7(), Guid.CreateVersion7(), 60, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Guid ChapterOf(PublishedEvent published, string kind) => published.Chapters.Single(chapter => chapter.Kind == kind).ChapterId;

    private Task<long> EventsInAsync(Guid session, CancellationToken cancellationToken) =>
        CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id", session, cancellationToken);

    /// <summary>How many sessions Learning's projection holds for the learner on the version.</summary>
    private async Task<long> SessionsOfAsync(Guid learner, Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(
            "select count(*) from learning.mt_doc_learningsessionview where data->>'LearnerId' = @learner and data->>'CourseVersionId' = @version",
            connection);
        command.Parameters.AddWithValue("learner", learner.ToString());
        command.Parameters.AddWithValue("version", courseVersionId.ToString());
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<PublishedEvent> PublishedToLearningAsync(CancellationToken cancellationToken)
    {
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);
        return published;
    }
}
