using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The learner answers a question of their open attempt: each answer is stored as an event and the last one counts.
/// The domain refuses first (no open attempt, a question outside it); only then are the options checked against the
/// version's copy. A refused answer stores nothing.
/// </summary>
public class AnswerQuestionTests
{
    private readonly AttemptFixture fixture = new();

    [Fact]
    public async Task An_answer_is_stored_and_shown_as_chosen()
    {
        await StartedAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(2));

        var answered = await fixture.AnswerAsync(Q1, A);

        answered.IsSuccess.ShouldBeTrue();
        var stored = fixture.Stream().OfType<QuestionAnswered>().ShouldHaveSingleItem();
        stored.Question.ShouldBe(Q1);
        stored.Chosen.ShouldBe([A]);
        stored.AnsweredAt.ShouldBe(Timestamp.From(fixture.Time.GetUtcNow()));
        var current = (await fixture.CurrentAttemptAsync()).Value;
        current.Questions.Single(question => question.Id == Q1.Value).ChosenOptionIds.ShouldBe([A.Value]);
        current.Questions.Single(question => question.Id == Q2.Value).ChosenOptionIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_later_answer_replaces_the_earlier_one()
    {
        await StartedAsync();

        (await fixture.AnswerAsync(Q1, D)).IsSuccess.ShouldBeTrue();
        (await fixture.AnswerAsync(Q1, A)).IsSuccess.ShouldBeTrue();

        fixture.Stream().OfType<QuestionAnswered>().Select(answered => answered.Chosen.Single()).ShouldBe([D, A]);
        (await fixture.CurrentAttemptAsync()).Value.Questions.Single(question => question.Id == Q1.Value).ChosenOptionIds.ShouldBe([A.Value]);
    }

    [Fact]
    public async Task Several_options_of_a_multiple_choice_question_are_accepted()
    {
        await StartedAsync();

        var answered = await fixture.AnswerAsync(Q2, C, B);

        answered.IsSuccess.ShouldBeTrue();
        fixture.Stream().OfType<QuestionAnswered>().ShouldHaveSingleItem().Chosen.ShouldBe([C, B], ignoreOrder: true);
    }

    [Fact]
    public async Task Two_options_on_a_single_choice_question_are_refused()
    {
        await StartedAsync();

        var answered = await fixture.AnswerAsync(Q1, A, D);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.single-choice-takes-one-option");
        answered.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ShouldHaveStoredNoAnswer();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_option_of_another_question_is_refused(bool ofTheOtherQuestion)
    {
        await StartedAsync();
        var option = ofTheOtherQuestion ? B : AnswerOptionId.From(Guid.CreateVersion7());

        var answered = await fixture.AnswerAsync(Q1, option);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.option-not-in-question");
        answered.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ShouldHaveStoredNoAnswer();
    }

    [Fact]
    public async Task A_question_outside_the_attempt_is_not_found()
    {
        await StartedAsync();

        // An option of another question too: the domain's refusal comes before the check of the options.
        var answered = await fixture.AnswerAsync(QuestionVersionId.From(Guid.CreateVersion7()), B);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.question-not-in-attempt");
        answered.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        ShouldHaveStoredNoAnswer();
    }

    [Fact]
    public async Task Answering_without_an_open_attempt_is_refused()
    {
        await fixture.OpenAsync();

        // Two options on a single choice question too: the domain's refusal comes first.
        var answered = await fixture.AnswerAsync(Q1, A, D);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.no-open-attempt");
        answered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Answering_after_the_submit_is_refused()
    {
        await StartedAsync();
        (await fixture.SubmitAsync()).IsSuccess.ShouldBeTrue();
        var stored = fixture.Stream().Count;

        var answered = await fixture.AnswerAsync(Q1, A);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.no-open-attempt");
        answered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().Count.ShouldBe(stored);
    }

    [Fact]
    public async Task Another_persons_examination_is_not_found()
    {
        await StartedAsync();

        var answered = await fixture.AnswerAsync(PersonId.From(Guid.CreateVersion7()), Q1, A);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("assessment.examination-not-found");
        answered.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        ShouldHaveStoredNoAnswer();
    }

    /// <summary>
    /// An answer whose save PostgreSQL ended as a deadlock with another save is told to try again
    /// (assessment.concurrent-save), not that the examination changed, and stores nothing; the answer saver sends it again.
    /// </summary>
    [Fact]
    public async Task An_answer_whose_save_deadlocked_is_refused_as_a_concurrent_save_and_stores_nothing()
    {
        await StartedAsync();
        fixture.Store.LoseNextSave(LostRaces.Deadlocked("40P01"));

        var answered = await fixture.AnswerAsync(Q1, A);

        answered.IsFailure.ShouldBeTrue("the deadlocked append was answered as a success");
        answered.Error.Code.ShouldBe("assessment.concurrent-save");
        answered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().OfType<QuestionAnswered>().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lost_race_is_refused()
    {
        await StartedAsync();
        fixture.Store.LoseNextSave();

        var answered = await fixture.AnswerAsync(Q1, A);

        answered.IsFailure.ShouldBeTrue("the lost append was answered as a success");
        answered.Error.Code.ShouldBe("assessment.examination-changed");
        answered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().OfType<QuestionAnswered>().ShouldBeEmpty();
    }

    /// <summary>The examination with an open attempt, started through the use case; the steps start counting after it.</summary>
    private async Task StartedAsync()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        fixture.Steps.Clear();
    }

    private void ShouldHaveStoredNoAnswer()
    {
        fixture.Stream().OfType<QuestionAnswered>().ShouldBeEmpty();
        fixture.Saves.ShouldBe(0);
    }
}
