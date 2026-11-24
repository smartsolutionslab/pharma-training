using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The learner starts an attempt at their own examination: the definition's questions in the order derived from the
/// attempt's id, stored in AttemptStarted, and each question's options in their own derived order, shown with their id
/// and text only.
/// </summary>
public class StartAttemptTests
{
    private readonly AttemptFixture fixture = new();

    [Fact]
    public async Task Starting_stores_the_definitions_questions_in_the_attempts_derived_order()
    {
        await fixture.OpenAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        var started = await fixture.StartAsync();

        started.IsSuccess.ShouldBeTrue();
        var stored = fixture.Stream().OfType<AttemptStarted>().ShouldHaveSingleItem();
        stored.Attempt.ShouldBe(started.Value);
        stored.Number.ShouldBe(AttemptNumber.First);
        stored.Questions.ShouldBe(AttemptShuffle.Questions(started.Value, [Q1, Q2]));
        stored.StartedAt.ShouldBe(Timestamp.From(fixture.Time.GetUtcNow()));
        fixture.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task The_current_attempt_shows_the_options_in_the_derived_order_with_id_and_text_only()
    {
        await fixture.OpenAsync();
        var started = await fixture.StartAsync();

        var current = await fixture.CurrentAttemptAsync();

        current.IsSuccess.ShouldBeTrue();
        current.Value.Id.ShouldBe(started.Value.Value);
        current.Value.Number.ShouldBe(1);
        current.Value.Questions.Select(question => question.Id).ShouldBe(AttemptShuffle.Questions(started.Value, [Q1, Q2]).Select(question => question.Value));

        var single = current.Value.Questions.Single(question => question.Id == Q1.Value);
        single.Type.ShouldBe(AssessmentQuestionType.SingleChoice);
        single.Text.ShouldBe(Q1Text);
        single.Options.ShouldBe([.. AttemptShuffle.Options(started.Value, [A, D]).Select(option => new AssessmentAnswerOptionDto(option.Value, TextOf(option)))]);
        single.ChosenOptionIds.ShouldBeEmpty();

        var multiple = current.Value.Questions.Single(question => question.Id == Q2.Value);
        multiple.Type.ShouldBe(AssessmentQuestionType.MultipleChoice);
        multiple.Text.ShouldBe(Q2Text);
        multiple.Options.ShouldBe([.. AttemptShuffle.Options(started.Value, [B, C, E]).Select(option => new AssessmentAnswerOptionDto(option.Value, TextOf(option)))]);
        multiple.ChosenOptionIds.ShouldBeEmpty();

        typeof(AssessmentAnswerOptionDto).GetProperties().Select(property => property.Name).ShouldBe(["Id", "Text"], ignoreOrder: true);
    }

    [Fact]
    public async Task Starting_while_an_attempt_is_open_is_refused()
    {
        await fixture.OpenAsync();
        (await fixture.StartAsync()).IsSuccess.ShouldBeTrue();
        var saves = fixture.Saves;

        var again = await fixture.StartAsync();

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("assessment.examination-not-unlocked");
        again.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().OfType<AttemptStarted>().ShouldHaveSingleItem();
        fixture.Saves.ShouldBe(saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_passed_or_exhausted_examination_refuses_a_start(bool passed)
    {
        await fixture.OpenAsync();
        if (passed)
        {
            await fixture.AttemptThroughTheDomainAsync([A], [B, C]);
        }
        else
        {
            await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
            await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
            await fixture.AttemptThroughTheDomainAsync([D], [B, C]);
        }

        var stored = fixture.Stream().Count;

        var started = await fixture.StartAsync();

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("assessment.examination-not-unlocked");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().Count.ShouldBe(stored);
    }

    [Fact]
    public async Task Starting_before_the_definition_arrives_is_refused()
    {
        await fixture.OpenAsync(withDefinition: false);

        var started = await fixture.StartAsync();

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("assessment.test-definition-missing");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().ShouldHaveSingleItem().ShouldBeOfType<ExaminationOpened>();
        fixture.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Another_persons_examination_is_not_found()
    {
        await fixture.OpenAsync();

        var started = await fixture.StartAsync(by: PersonId.From(Guid.CreateVersion7()));

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("assessment.examination-not-found");
        started.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        fixture.Stream().ShouldHaveSingleItem();
        fixture.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_examination_is_not_found()
    {
        await fixture.OpenAsync();

        var started = await fixture.StartAsync(examination: ExaminationId.New());

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("assessment.examination-not-found");
        started.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        fixture.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_start_that_lost_the_race_is_refused_and_stores_nothing()
    {
        await fixture.OpenAsync();
        fixture.Store.LoseNextSave();

        var started = await fixture.StartAsync();

        started.IsFailure.ShouldBeTrue("the lost append was answered as a success");
        started.Error.Code.ShouldBe("assessment.examination-changed");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        fixture.Stream().ShouldHaveSingleItem().ShouldBeOfType<ExaminationOpened>();
    }

    [Fact]
    public async Task Each_attempt_orders_the_questions_by_its_own_id()
    {
        await fixture.OpenAsync();
        var first = await fixture.StartAsync();
        (await fixture.SubmitAsync()).IsSuccess.ShouldBeTrue();

        var second = await fixture.StartAsync();

        var stored = fixture.Stream().OfType<AttemptStarted>().ToList();
        stored.Count.ShouldBe(2);
        stored[0].Questions.ShouldBe(AttemptShuffle.Questions(first.Value, [Q1, Q2]));
        stored[1].Questions.ShouldBe(AttemptShuffle.Questions(second.Value, [Q1, Q2]));
        stored[1].Number.ShouldBe(AttemptNumber.From(2));
    }
}
