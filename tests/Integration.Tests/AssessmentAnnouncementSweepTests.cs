using System.Net;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentAttempts;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.AssessmentDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A test pass Assessment stored but never announced is announced by its sweep (#277), so Certification issues the
/// certificate and Learning finishes the session. The crash between the submit's append and its outbox commit is
/// simulated by its outcome: an <c>ExaminationPassed</c> in the examination's stream, with no marker and no envelope. A
/// submit that announces its pass records the marker in the same commit, so the sweep never announces it again.
/// </summary>
public class AssessmentAnnouncementSweepTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_stored_test_pass_that_was_never_announced_issues_the_certificate_and_finishes_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var learner = LearningSessions.SubjectOf(client);

        // Learning and Certification both hold the person's membership and the version, each from one fanned-out send.
        await DeliverToLearningAndCertificationAsync(
            LearningDatabase.StartedType, LearningDatabase.Started(Guid.CreateVersion7(), learner, Guid.CreateVersion7()), cancellationToken);
        var published = LearningDatabase.Published(Guid.CreateVersion7(), LearningDatabase.ShortVideoSeconds);
        await DeliverToLearningAndCertificationAsync(LearningDatabase.PublishedType, published, cancellationToken);
        var session = await StartedAsync(client, published, cancellationToken);

        // Learning's completion of that session, as Learning announces it, opens the examination.
        await DeliverAsync(
            aspire,
            CompletedType,
            new SentCompletion(session.Id, learner, published.CourseId, published.CourseVersionId, new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow())),
            cancellationToken);
        var examination = await ReadExaminationAsync(client, session.Id, cancellationToken);
        var attempt = Guid.CreateVersion7();

        var version = await PlantPassAsync(
            aspire,
            new PlantedExaminationPassed(examination.Id, session.Id, learner, published.CourseId, published.CourseVersionId, attempt, 90, TimeProvider.System.GetUtcNow()),
            cancellationToken);

        await AnnouncementSweeps.WaitUntilAsync(
            async () => (await CertificationIssuing.CertificatesAsync(aspire, attempt, cancellationToken)).Count > 0,
            $"Certification issued no certificate for attempt {attempt}, whose stored pass was never announced,",
            cancellationToken);
        await AnnouncementSweeps.WaitUntilAsync(
            async () => (await ReadSessionAsync(client, session.Id, cancellationToken)).Status == "Finished",
            $"Learning did not finish session {session.Id}, whose stored pass was never announced,",
            cancellationToken);
        (await CertificationIssuing.CertificatesAsync(aspire, attempt, cancellationToken)).ShouldHaveSingleItem();
        (await CountAnnouncedAsync(aspire, examination.Id, version, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_passing_submit_records_its_announcement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var opened = await OpenExaminationAsync(aspire, client, cancellationToken);

        var result = await AttemptAsync(client, opened, Right, cancellationToken);

        result.Outcome.ShouldBe("Passed");
        var version = await StoredVersionAsync(aspire, opened.ExaminationId, "examination_passed", cancellationToken);
        (await CountAnnouncedAsync(aspire, opened.ExaminationId, version, cancellationToken)).ShouldBe(1, "the submit's commit did not record the announcement it carried");
    }

    /// <summary>Sends the event once, as its publisher does; the broker fans it out, and both Learning's and Certification's inboxes handle it under the one envelope id.</summary>
    private async Task DeliverToLearningAndCertificationAsync<T>(string type, T message, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await Broker.SendAsync(aspire, type, message, messageId, cancellationToken);
        await LearningDatabase.WaitUntilHandledAsync(aspire, messageId, cancellationToken);
        await CertificationDatabase.WaitUntilHandledAsync(aspire, messageId, cancellationToken);
    }

    private static async Task<LearningSessions.SessionBody> StartedAsync(HttpClient client, LearningDatabase.PublishedEvent published, CancellationToken cancellationToken)
    {
        using var started = await LearningSessions.StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        return await LearningSessions.BodyOfAsync(started, cancellationToken);
    }

    private static async Task<LearningSessions.SessionBody> ReadSessionAsync(HttpClient client, Guid session, CancellationToken cancellationToken)
    {
        using var read = await LearningSessions.ReadSessionAsync(client, session, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        return await LearningSessions.BodyOfAsync(read, cancellationToken);
    }
}
