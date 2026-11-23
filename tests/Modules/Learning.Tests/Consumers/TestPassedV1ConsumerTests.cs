using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions.InMemoryLearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Assessment's <see cref="TestPassedV1"/> finishes the session (#42) at the time the test was passed, from any state,
/// once; a fault throws so it is retried and then dead-lettered, never dropped (ADR 0018, ADR 0020).
/// </summary>
public class TestPassedV1ConsumerTests
{
    private readonly OutcomeSessions sessions = new();

    [Theory]
    [InlineData(SessionStatus.ContentCompleted)]
    [InlineData(SessionStatus.InProgress)]
    public async Task A_passed_test_finishes_the_session_at_the_time_it_was_passed(SessionStatus before)
    {
        var session = before == SessionStatus.ContentCompleted ? await sessions.CompletedAsync() : await sessions.StartedAsync();
        (await sessions.LoadAsync(session)).Status.ShouldBe(before);
        var stored = sessions.Stream(session).Count;
        var passed = sessions.Passed(session);

        await HandleAsync(passed);

        var finished = sessions.Stream(session).Skip(stored).ShouldHaveSingleItem().ShouldBeOfType<SessionFinished>();
        finished.Session.ShouldBe(session);
        finished.FinishedAt.ShouldBe(Timestamp.From(passed.PassedAt));
        (await sessions.LoadAsync(session)).Status.ShouldBe(SessionStatus.Finished);
    }

    [Fact]
    public async Task A_passed_test_delivered_again_or_republished_under_a_new_identifier_finishes_once()
    {
        var session = await sessions.CompletedAsync();
        var saves = sessions.Store.Saves;
        var passed = sessions.Passed(session);

        await HandleAsync(passed);
        await HandleAsync(OutcomeSessions.Redelivered(passed));
        await HandleAsync(OutcomeSessions.Republished(passed));

        sessions.Stream(session).OfType<SessionFinished>().ShouldHaveSingleItem();
        sessions.Store.Saves.ShouldBe(saves + 1);
    }

    [Fact]
    public async Task An_unknown_session_throws_naming_it()
    {
        var unknown = LearningSessionId.New();
        var saves = sessions.Store.Saves;

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(sessions.Passed(unknown)));

        refused.Message.ShouldContain(unknown.Value.ToString());
        sessions.Store.Saves.ShouldBe(saves);
        sessions.Store.Streams.ShouldNotContainKey(unknown.Value);
    }

    [Theory]
    [InlineData(nameof(TestPassedV1.PersonId))]
    [InlineData(nameof(TestPassedV1.CourseId))]
    [InlineData(nameof(TestPassedV1.CourseVersionId))]
    public async Task A_passed_test_that_names_another_learner_course_or_version_throws_naming_the_field(string field)
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        var other = Guid.CreateVersion7();
        var passed = sessions.Passed(session);
        var mismatched = field switch
        {
            nameof(TestPassedV1.PersonId) => passed with { PersonId = other },
            nameof(TestPassedV1.CourseId) => passed with { CourseId = other },
            nameof(TestPassedV1.CourseVersionId) => passed with { CourseVersionId = other },
            _ => throw new InvalidOperationException($"{field} is no field the session is compared on."),
        };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(mismatched));

        refused.Message.ShouldContain(field);
        refused.Message.ShouldContain(other.ToString());
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
    }

    [Theory]
    [InlineData(nameof(TestPassedV1.SessionId))]
    [InlineData(nameof(TestPassedV1.PersonId))]
    [InlineData(nameof(TestPassedV1.CourseId))]
    [InlineData(nameof(TestPassedV1.CourseVersionId))]
    public async Task A_malformed_passed_test_is_refused_naming_its_field(string field)
    {
        var session = await sessions.CompletedAsync();
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;
        var passed = sessions.Passed(session);
        var malformed = field switch
        {
            nameof(TestPassedV1.SessionId) => passed with { SessionId = Guid.Empty },
            nameof(TestPassedV1.PersonId) => passed with { PersonId = Guid.Empty },
            nameof(TestPassedV1.CourseId) => passed with { CourseId = Guid.Empty },
            nameof(TestPassedV1.CourseVersionId) => passed with { CourseVersionId = Guid.Empty },
            _ => throw new InvalidOperationException($"{field} is no field of the passed test that is checked."),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => HandleAsync(malformed));

        $"{refused.ParamName} {refused.Message}".ShouldContain(field);
        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_passed_test_that_loses_the_race_throws_and_its_retry_finishes_once()
    {
        var session = await sessions.CompletedAsync();
        var passed = sessions.Passed(session);
        sessions.Store.LoseNextSave();

        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(passed));
        await HandleAsync(OutcomeSessions.Redelivered(passed));

        sessions.Stream(session).OfType<SessionFinished>().ShouldHaveSingleItem();
        (await sessions.LoadAsync(session)).Status.ShouldBe(SessionStatus.Finished);
    }

    [Fact]
    public async Task A_passed_test_publishes_nothing()
    {
        var session = await sessions.CompletedAsync();
        sessions.Store.Steps.Clear();

        await HandleAsync(sessions.Passed(session));

        sessions.Store.Steps.ShouldBe([Saved], "the passed test should store the finish and publish nothing");
        sessions.Store.Published.ShouldBeEmpty();
        sessions.Store.Commits.ShouldBe(0);
    }

    private Task HandleAsync(TestPassedV1 message) =>
        new TestPassedV1Consumer(sessions.Store).HandleAsync(message, TestContext.Current.CancellationToken);
}
