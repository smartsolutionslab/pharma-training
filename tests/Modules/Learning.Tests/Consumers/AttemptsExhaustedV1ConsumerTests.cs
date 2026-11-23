using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions.InMemoryLearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Assessment's <see cref="AttemptsExhaustedV1"/> reopens the chapters of the wrong answers (#42): once per announcement,
/// whatever happened to the session since, never in a finished session, and a fault throws so it is retried and then
/// dead-lettered, never dropped (ADR 0025).
/// </summary>
public class AttemptsExhaustedV1ConsumerTests
{
    private readonly OutcomeSessions sessions = new();

    [Fact]
    public async Task An_exhaustion_reopens_the_failed_chapters_of_a_completed_session()
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        sessions.Time.Advance(TimeSpan.FromMinutes(30));
        var exhausted = sessions.Exhausted(session, sessions.Handbook);

        await HandleAsync(exhausted);

        var appended = sessions.Stream(session).Skip(stored).ToList();
        appended.Count.ShouldBe(2, "the exhaustion appended something other than the reopen and its note");
        var reopened = appended[0].ShouldBeOfType<ChaptersReopened>();
        reopened.Chapters.ShouldBe([sessions.Handbook]);
        reopened.ReopenedAt.ShouldBe(sessions.Now());
        appended[1].ShouldBeOfType<ExhaustionNoted>().Notice.ShouldBe(ExhaustionNotice.From(exhausted.Metadata.EventIdentifier));
        sessions.Store.Saves.ShouldBe(saves + 1);
        var reloaded = await sessions.LoadAsync(session);
        reloaded.Status.ShouldBe(SessionStatus.InProgress);
        reloaded.CompletedChapters.ShouldBe([sessions.Intro, sessions.Film], ignoreOrder: true);
    }

    [Fact]
    public async Task The_same_exhaustion_delivered_again_after_the_chapters_were_worked_through_changes_nothing()
    {
        var session = await sessions.CompletedAsync();
        var exhausted = sessions.Exhausted(session, sessions.Handbook);
        await HandleAsync(exhausted);
        await sessions.ChangeAsync(session, reopened => reopened.RecordProgress(sessions.Handbook, ProgressPercent.Complete, sessions.Now()));
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;

        await HandleAsync(OutcomeSessions.Redelivered(exhausted));

        sessions.Stream(session).OfType<ChaptersReopened>().Count().ShouldBe(1, "the exhaustion reopened the chapters never, or again when delivered a second time");
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
        (await sessions.LoadAsync(session)).Status.ShouldBe(SessionStatus.ContentCompleted);
    }

    [Fact]
    public async Task A_new_exhaustion_after_the_content_was_completed_again_reopens_again()
    {
        var session = await sessions.CompletedAsync();
        await HandleAsync(sessions.Exhausted(session, sessions.Handbook));
        await sessions.ChangeAsync(session, reopened => reopened.RecordProgress(sessions.Handbook, ProgressPercent.Complete, sessions.Now()));

        await HandleAsync(sessions.Exhausted(session, sessions.Handbook));

        sessions.Stream(session).OfType<ChaptersReopened>().Count().ShouldBe(2);
        sessions.Stream(session).OfType<ExhaustionNoted>().Count().ShouldBe(2);
        (await sessions.LoadAsync(session)).Status.ShouldBe(SessionStatus.InProgress);
    }

    [Fact]
    public async Task An_exhaustion_of_a_finished_session_changes_nothing()
    {
        var session = await sessions.CompletedAsync();
        await sessions.ChangeAsync(session, completed => completed.Finish(sessions.Now()));
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;

        await HandleAsync(sessions.Exhausted(session, sessions.Handbook));

        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
        (await sessions.LoadAsync(session)).Status.ShouldBe(SessionStatus.Finished);
    }

    [Fact]
    public async Task An_exhaustion_of_an_unknown_session_throws_naming_it()
    {
        var unknown = LearningSessionId.New();
        var saves = sessions.Store.Saves;

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(sessions.Exhausted(unknown, sessions.Handbook)));

        refused.Message.ShouldContain(unknown.Value.ToString());
        sessions.Store.Saves.ShouldBe(saves);
        sessions.Store.Streams.ShouldNotContainKey(unknown.Value);
    }

    [Theory]
    [InlineData(nameof(AttemptsExhaustedV1.PersonId))]
    [InlineData(nameof(AttemptsExhaustedV1.CourseId))]
    [InlineData(nameof(AttemptsExhaustedV1.CourseVersionId))]
    public async Task An_exhaustion_that_names_another_learner_course_or_version_throws_naming_the_field(string field)
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        var other = Guid.CreateVersion7();
        var exhausted = sessions.Exhausted(session, sessions.Handbook);
        var mismatched = field switch
        {
            nameof(AttemptsExhaustedV1.PersonId) => exhausted with { PersonId = other },
            nameof(AttemptsExhaustedV1.CourseId) => exhausted with { CourseId = other },
            nameof(AttemptsExhaustedV1.CourseVersionId) => exhausted with { CourseVersionId = other },
            _ => throw new InvalidOperationException($"{field} is no field the session is compared on."),
        };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(mismatched));

        refused.Message.ShouldContain(field);
        refused.Message.ShouldContain(other.ToString());
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task An_exhaustion_naming_a_chapter_outside_the_session_throws_with_the_domains_code()
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        var outside = ChapterId.From(Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(sessions.Exhausted(session, sessions.Handbook, outside)));

        refused.Message.ShouldContain("learning.chapter-not-in-course");
        refused.Message.ShouldContain(outside.Value.ToString());
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
    }

    [Theory]
    [InlineData("empty session id", nameof(AttemptsExhaustedV1.SessionId))]
    [InlineData("empty person id", nameof(AttemptsExhaustedV1.PersonId))]
    [InlineData("empty course id", nameof(AttemptsExhaustedV1.CourseId))]
    [InlineData("empty course version id", nameof(AttemptsExhaustedV1.CourseVersionId))]
    [InlineData("no failed chapters", nameof(AttemptsExhaustedV1.FailedChapterIds))]
    [InlineData("an empty failed chapter id", nameof(AttemptsExhaustedV1.FailedChapterIds))]
    [InlineData("an empty event identifier", nameof(EventMetadata.EventIdentifier))]
    public async Task A_malformed_exhaustion_is_refused_naming_its_field(string malformation, string field)
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        var exhausted = sessions.Exhausted(session, sessions.Handbook);
        var malformed = malformation switch
        {
            "empty session id" => exhausted with { SessionId = Guid.Empty },
            "empty person id" => exhausted with { PersonId = Guid.Empty },
            "empty course id" => exhausted with { CourseId = Guid.Empty },
            "empty course version id" => exhausted with { CourseVersionId = Guid.Empty },
            "no failed chapters" => exhausted with { FailedChapterIds = [] },
            "an empty failed chapter id" => exhausted with { FailedChapterIds = [sessions.Handbook.Value, Guid.Empty] },
            "an empty event identifier" => exhausted with { Metadata = new EventMetadata(Guid.Empty, sessions.Time.GetUtcNow()) },
            _ => throw new InvalidOperationException($"{malformation} is no malformation of the exhaustion."),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(malformed));

        $"{refused.ParamName} {refused.Message}".ShouldContain(field);
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task An_exhaustion_that_loses_the_race_on_its_stream_throws_and_its_retry_reopens_once()
    {
        var session = await sessions.CompletedAsync();
        var exhausted = sessions.Exhausted(session, sessions.Handbook);
        sessions.Store.LoseNextSave();

        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(exhausted));
        await HandleAsync(OutcomeSessions.Redelivered(exhausted));

        sessions.Stream(session).OfType<ChaptersReopened>().ShouldHaveSingleItem();
        sessions.Stream(session).OfType<ExhaustionNoted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_exhaustion_publishes_nothing()
    {
        var session = await sessions.CompletedAsync();
        sessions.Store.Steps.Clear();

        await HandleAsync(sessions.Exhausted(session, sessions.Handbook));

        sessions.Store.Steps.ShouldBe([Saved], "the exhaustion should store the reopen and publish nothing");
        sessions.Store.Published.ShouldBeEmpty();
        sessions.Store.Commits.ShouldBe(0);
    }

    private Task HandleAsync(AttemptsExhaustedV1 message) =>
        new AttemptsExhaustedV1Consumer(sessions.Store, sessions.Time).HandleAsync(message, TestContext.Current.CancellationToken);
}
