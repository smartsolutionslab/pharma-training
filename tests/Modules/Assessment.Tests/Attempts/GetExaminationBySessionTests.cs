using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The learner's client knows its learning session and reads the examination by it, with the id the attempt routes
/// need. Examination ids are derivable, so another person's examination is not found, exactly like an unknown one.
/// </summary>
public class GetExaminationBySessionTests
{
    private readonly AttemptFixture fixture = new();

    [Fact]
    public async Task The_learners_examination_is_found_by_its_session_with_its_id_status_and_attempts()
    {
        await fixture.OpenAsync();

        var read = await fixture.BySessionAsync();

        read.IsSuccess.ShouldBeTrue();
        read.Value.ShouldBe(new AssessmentExaminationDto(
            ExaminationId.For(fixture.Session).Value,
            fixture.Session.Value,
            fixture.Course.Value,
            fixture.Version.Value,
            AssessmentExaminationStatus.Unlocked,
            AttemptsUsed: 0,
            MaxAttempts: 3,
            CurrentAttempt: null,
            LastResult: null));
    }

    [Fact]
    public async Task Another_persons_examination_is_not_found()
    {
        await fixture.OpenAsync();

        var read = await fixture.BySessionAsync(by: Guid.CreateVersion7());

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe("assessment.examination-not-found");
        read.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_or_empty_session_is_not_found(bool empty)
    {
        await fixture.OpenAsync();

        var read = await fixture.BySessionAsync(session: empty ? Guid.Empty : Guid.CreateVersion7());

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe("assessment.examination-not-found");
        read.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Before_the_definition_arrives_max_attempts_is_null()
    {
        await fixture.OpenAsync(withDefinition: false);

        var read = await fixture.BySessionAsync();

        read.IsSuccess.ShouldBeTrue();
        read.Value.Status.ShouldBe(AssessmentExaminationStatus.Unlocked);
        read.Value.MaxAttempts.ShouldBeNull();
        read.Value.AttemptsUsed.ShouldBe(0);
    }

    [Fact]
    public async Task An_open_attempt_is_shown_as_current_and_counts_as_used()
    {
        await fixture.OpenAsync();
        var started = await fixture.StartAsync();
        (await fixture.AnswerAsync(Q2, C, B)).IsSuccess.ShouldBeTrue();

        var read = await fixture.BySessionAsync();

        read.Value.Status.ShouldBe(AssessmentExaminationStatus.InAttempt);
        read.Value.AttemptsUsed.ShouldBe(1);
        read.Value.LastResult.ShouldBeNull();
        var current = read.Value.CurrentAttempt.ShouldNotBeNull();
        current.Id.ShouldBe(started.Value.Value);
        current.Number.ShouldBe(1);
        current.Questions.Select(question => question.Id).ShouldBe(AttemptShuffle.Questions(started.Value, [Q1, Q2]).Select(question => question.Value));
        current.Questions.Single(question => question.Id == Q2.Value).ChosenOptionIds.ShouldBe([C.Value, B.Value], ignoreOrder: true);
        current.Questions.Single(question => question.Id == Q1.Value).ChosenOptionIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_a_failed_attempt_the_last_result_is_shown_and_no_current_attempt()
    {
        await fixture.OpenAsync();
        var submitted = await fixture.AttemptAsync([D], [B, C]);

        var read = await fixture.BySessionAsync();

        read.Value.Status.ShouldBe(AssessmentExaminationStatus.Unlocked);
        read.Value.AttemptsUsed.ShouldBe(1);
        read.Value.MaxAttempts.ShouldBe(3);
        read.Value.CurrentAttempt.ShouldBeNull();
        var last = read.Value.LastResult.ShouldNotBeNull();
        (last.AttemptId, last.Number, last.ScorePercent, last.PassingScorePercent, last.Outcome, last.AttemptsLeft)
            .ShouldBe((submitted.Value.Value, 1, 50, 80, AssessmentAttemptOutcome.Failed, 2));
        last.ChaptersToRepeat.ShouldBe([Hygiene.Value]);
    }
}
