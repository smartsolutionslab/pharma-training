using System.Net;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The learner submits their open attempt and the server scores it against the version's copy, without partial
/// credit. A pass is stored, then announced as TestPassedV1 for Learning's session; a failure as TestFailedV1, and the
/// last allowed failure also as AttemptsExhaustedV1 with every chapter failed since the last unlock. The result shows
/// the score, the outcome, the chapters to repeat and the attempts left, never which answer was right.
/// </summary>
public class SubmitAttemptTests
{
    private readonly AttemptFixture fixture = new();

    [Fact]
    public async Task All_right_passes_with_100_and_publishes_one_TestPassedV1_after_saving()
    {
        await fixture.OpenAsync();
        var started = await fixture.StartAsync();
        await fixture.AnswerAsync(Q1, A);
        await fixture.AnswerAsync(Q2, C, B);
        fixture.Steps.Clear();

        var submitted = await fixture.SubmitAsync();

        submitted.IsSuccess.ShouldBeTrue();
        submitted.Value.ShouldBe(started.Value);
        fixture.UnitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestPassedV1>();
        fixture.UnitOfWork.Commits.ShouldBe(1);
        fixture.Stream()[^1].ShouldBeOfType<ExaminationPassed>();
        fixture.Steps.ShouldBe([InMemoryAssessmentAggregateStore.Saved, RecordingAssessmentUnitOfWork.Queued, RecordingAssessmentUnitOfWork.Committed]);

        var result = (await fixture.ResultAsync(submitted.Value)).Value;
        (result.AttemptId, result.Number, result.ScorePercent, result.PassingScorePercent, result.Outcome, result.AttemptsLeft)
            .ShouldBe((started.Value.Value, 1, 100, 80, AssessmentAttemptOutcome.Passed, 0));
        result.ChaptersToRepeat.ShouldBeEmpty();
    }

    [Fact]
    public async Task TestPassedV1_names_learnings_session_not_the_examination()
    {
        await fixture.OpenAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));

        var submitted = await fixture.AttemptAsync([A], [B, C]);

        var passed = fixture.UnitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestPassedV1>();
        passed.SessionId.ShouldBe(fixture.Session.Value);
        passed.SessionId.ShouldNotBe(ExaminationId.For(fixture.Session).Value);
        passed.PersonId.ShouldBe(fixture.Learner.Value);
        passed.AttemptId.ShouldBe(submitted.Value.Value);
        passed.CourseId.ShouldBe(fixture.Course.Value);
        passed.CourseVersionId.ShouldBe(fixture.Version.Value);
        passed.ScorePercent.ShouldBe(100);
        passed.PassedAt.ShouldBe(fixture.Time.GetUtcNow());
    }

    /// <summary>The issue's reference test at the use case: two of the three options of the multiple choice question are no answer to it.</summary>
    [Fact]
    public async Task Two_of_three_right_options_score_no_partial_credit()
    {
        await fixture.OpenAsync();

        var submitted = await fixture.AttemptAsync([A], [B]);

        var result = (await fixture.ResultAsync(submitted.Value)).Value;
        result.ScorePercent.ShouldBe(50);
        result.Outcome.ShouldBe(AssessmentAttemptOutcome.Failed);
        result.ChaptersToRepeat.ShouldBe([Gloves.Value]);
        fixture.UnitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestFailedV1>();
    }

    [Fact]
    public async Task An_unanswered_question_is_wrong()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        (await fixture.AnswerAsync(Q2, B, C)).IsSuccess.ShouldBeTrue();

        var submitted = await fixture.SubmitAsync();

        submitted.IsSuccess.ShouldBeTrue();
        var result = (await fixture.ResultAsync(submitted.Value)).Value;
        result.ScorePercent.ShouldBe(50);
        result.ChaptersToRepeat.ShouldBe([Hygiene.Value]);
    }

    [Fact]
    public async Task A_failed_attempt_publishes_one_TestFailedV1_after_saving_and_names_its_chapters_to_repeat()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        await fixture.AnswerAsync(Q1, D);
        await fixture.AnswerAsync(Q2, B, C);
        fixture.Steps.Clear();

        var submitted = await fixture.SubmitAsync();

        submitted.IsSuccess.ShouldBeTrue();
        fixture.UnitOfWork.Published.ShouldHaveSingleItem().ShouldBeOfType<TestFailedV1>();
        fixture.UnitOfWork.Commits.ShouldBe(1);
        fixture.Steps.ShouldBe([InMemoryAssessmentAggregateStore.Saved, RecordingAssessmentUnitOfWork.Queued, RecordingAssessmentUnitOfWork.Committed]);
        fixture.Stream()[^1].ShouldBeOfType<AttemptSubmitted>().Outcome.ShouldBe(AttemptOutcome.Failed);
        var result = (await fixture.ResultAsync(submitted.Value)).Value;
        (result.Number, result.ScorePercent, result.PassingScorePercent, result.Outcome, result.AttemptsLeft)
            .ShouldBe((1, 50, 80, AssessmentAttemptOutcome.Failed, 2));
        result.ChaptersToRepeat.ShouldBe([Hygiene.Value]);
    }

    [Fact]
    public async Task The_third_failure_shows_every_chapter_failed_since_the_unlock_and_no_attempts_left()
    {
        await fixture.OpenAsync();
        await fixture.AttemptAsync([D], [B, C]);
        var second = await fixture.AttemptAsync([A], [B, E]);
        var secondResult = (await fixture.ResultAsync(second.Value)).Value;
        secondResult.ChaptersToRepeat.ShouldBe([Gloves.Value]);
        secondResult.AttemptsLeft.ShouldBe(1);

        var third = await fixture.AttemptAsync([D], [B, C]);

        var result = (await fixture.ResultAsync(third.Value)).Value;
        (result.Number, result.Outcome, result.AttemptsLeft).ShouldBe((3, AssessmentAttemptOutcome.Failed, 0));
        result.ChaptersToRepeat.ShouldBe([Hygiene.Value, Gloves.Value], ignoreOrder: true);
        (await fixture.BySessionAsync()).Value.Status.ShouldBe(AssessmentExaminationStatus.Exhausted);
    }

    [Fact]
    public async Task TestFailedV1_names_learnings_session_the_attempt_and_its_number()
    {
        await fixture.OpenAsync();
        await fixture.AttemptAsync([D], [B, C]);

        var second = await fixture.AttemptAsync([A], [B]);

        var failures = fixture.UnitOfWork.Published.Cast<TestFailedV1>().ToList();
        failures.Select(failure => failure.AttemptNumber).ShouldBe([1, 2]);
        var failed = failures[^1];
        failed.AttemptId.ShouldBe(second.Value.Value);
        failed.SessionId.ShouldBe(fixture.Session.Value);
        failed.SessionId.ShouldNotBe(fixture.Examination.Value);
        failed.PersonId.ShouldBe(fixture.Learner.Value);
        failed.CourseId.ShouldBe(fixture.Course.Value);
        failed.CourseVersionId.ShouldBe(fixture.Version.Value);
    }

    [Fact]
    public async Task The_third_failure_publishes_TestFailedV1_then_AttemptsExhaustedV1_with_every_failed_chapter_once()
    {
        await fixture.OpenAsync();
        await fixture.AttemptAsync([A], [B, E]);
        await fixture.AttemptAsync([D], [B, C]);
        fixture.Steps.Clear();
        fixture.UnitOfWork.Published.Clear();

        var third = await fixture.AttemptAsync([D], [B]);

        third.IsSuccess.ShouldBeTrue();
        fixture.UnitOfWork.Published.Count.ShouldBe(2);
        fixture.UnitOfWork.Published[0].ShouldBeOfType<TestFailedV1>().AttemptNumber.ShouldBe(3);
        var exhausted = fixture.UnitOfWork.Published[1].ShouldBeOfType<AttemptsExhaustedV1>();
        exhausted.SessionId.ShouldBe(fixture.Session.Value);
        exhausted.PersonId.ShouldBe(fixture.Learner.Value);
        exhausted.CourseId.ShouldBe(fixture.Course.Value);
        exhausted.CourseVersionId.ShouldBe(fixture.Version.Value);
        exhausted.FailedChapterIds.ShouldBe([Gloves.Value, Hygiene.Value]);
        fixture.UnitOfWork.Commits.ShouldBe(3);
        fixture.Steps.Count(step => step == RecordingAssessmentUnitOfWork.Committed).ShouldBe(1);
        fixture.Steps[^1].ShouldBe(RecordingAssessmentUnitOfWork.Committed);
        fixture.Stream()[^1].ShouldBeOfType<ExaminationExhausted>();
    }

    [Fact]
    public async Task An_exhaustion_after_an_unlock_names_only_the_chapters_failed_since_the_unlock()
    {
        await fixture.OpenAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
        }

        await fixture.ChangeAsync(examination => examination.Unlock(Timestamp.From(Now.AddDays(1))));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await fixture.AttemptAsync([A], [E]);
        }

        fixture.UnitOfWork.Published.OfType<TestFailedV1>().Select(failure => failure.AttemptNumber).ShouldBe([1, 2, 3]);
        fixture.UnitOfWork.Published.OfType<AttemptsExhaustedV1>().ShouldHaveSingleItem().FailedChapterIds.ShouldBe([Gloves.Value]);
    }

    [Fact]
    public async Task A_last_failure_that_lost_the_race_stores_and_publishes_nothing()
    {
        await fixture.OpenAsync();
        await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
        await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        await fixture.AnswerAsync(Q1, D);
        fixture.Steps.Clear();
        fixture.Store.LoseNextSave();

        var submitted = await fixture.SubmitAsync();

        submitted.Error.Code.ShouldBe("assessment.examination-changed");
        fixture.Stream().OfType<ExaminationExhausted>().ShouldBeEmpty();
        fixture.UnitOfWork.Published.ShouldBeEmpty();
        fixture.UnitOfWork.Commits.ShouldBe(0);
        fixture.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_passed_attempt_has_no_chapters_to_repeat_and_no_attempts_left()
    {
        await fixture.OpenAsync();
        await fixture.AttemptAsync([D], [B, C]);

        var passed = await fixture.AttemptAsync([A], [B, C]);

        var result = (await fixture.ResultAsync(passed.Value)).Value;
        (result.Number, result.ScorePercent, result.Outcome, result.AttemptsLeft).ShouldBe((2, 100, AssessmentAttemptOutcome.Passed, 0));
        result.ChaptersToRepeat.ShouldBeEmpty();
        (await fixture.BySessionAsync()).Value.Status.ShouldBe(AssessmentExaminationStatus.Passed);
    }

    [Fact]
    public async Task Submitting_without_an_open_attempt_is_refused()
    {
        await fixture.OpenAsync();

        var submitted = await fixture.SubmitAsync();

        submitted.IsFailure.ShouldBeTrue();
        submitted.Error.Code.ShouldBe("assessment.no-open-attempt");
        submitted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Saves.ShouldBe(0);
        fixture.UnitOfWork.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_submit_that_lost_the_race_stores_and_publishes_nothing()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        await fixture.AnswerAsync(Q1, A);
        await fixture.AnswerAsync(Q2, B, C);
        fixture.Steps.Clear();
        fixture.Store.LoseNextSave();

        var submitted = await fixture.SubmitAsync();

        submitted.IsFailure.ShouldBeTrue("the lost append was answered as a success");
        submitted.Error.Code.ShouldBe("assessment.examination-changed");
        submitted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().OfType<AttemptSubmitted>().ShouldBeEmpty();
        fixture.Stream().OfType<ExaminationPassed>().ShouldBeEmpty();
        fixture.UnitOfWork.Published.ShouldBeEmpty();
        fixture.UnitOfWork.Commits.ShouldBe(0);
        fixture.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_persons_examination_is_not_found()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        fixture.Steps.Clear();

        var submitted = await fixture.SubmitAsync(by: PersonId.From(Guid.CreateVersion7()));

        submitted.IsFailure.ShouldBeTrue();
        submitted.Error.Code.ShouldBe("assessment.examination-not-found");
        submitted.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        fixture.Saves.ShouldBe(0);
        fixture.UnitOfWork.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_persons_result_is_not_found()
    {
        await fixture.OpenAsync();
        var submitted = await fixture.AttemptAsync([A], [B, C]);

        var result = await fixture.ResultAsync(submitted.Value, by: Guid.CreateVersion7());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("assessment.examination-not-found");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
