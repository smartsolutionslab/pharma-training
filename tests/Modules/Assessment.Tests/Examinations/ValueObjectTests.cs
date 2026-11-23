using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class ValueObjectTests
{
    [Fact]
    public void A_score_is_a_percentage_from_0_to_100()
    {
        ScorePercent.From(0).Value.ShouldBe(0);
        ScorePercent.From(100).Value.ShouldBe(100);
        Should.Throw<GuardException>(() => ScorePercent.From(-1));
        Should.Throw<GuardException>(() => ScorePercent.From(101));
    }

    [Fact]
    public void A_score_of_some_right_answers_is_rounded_down() =>
        ScorePercent.Of(QuestionCount.From(2), QuestionCount.From(3)).ShouldBe(ScorePercent.From(66));

    [Fact]
    public void A_score_needs_at_least_one_question() =>
        Should.Throw<GuardException>(() => ScorePercent.Of(QuestionCount.From(0), QuestionCount.From(0)));

    [Fact]
    public void A_score_cannot_count_more_right_answers_than_questions() =>
        Should.Throw<GuardException>(() => ScorePercent.Of(QuestionCount.From(4), QuestionCount.From(3)));

    [Fact]
    public void Scores_compare_by_value()
    {
        (ScorePercent.From(60) > ScorePercent.From(40)).ShouldBeTrue();
        (ScorePercent.From(40) < ScorePercent.From(60)).ShouldBeTrue();
        (ScorePercent.From(60) >= ScorePercent.From(60)).ShouldBeTrue();
        (ScorePercent.From(60) <= ScorePercent.From(60)).ShouldBeTrue();
        ScorePercent.From(60).ShouldBe(ScorePercent.From(60));
    }

    [Fact]
    public void A_question_count_is_never_negative()
    {
        QuestionCount.From(0).Value.ShouldBe(0);
        Should.Throw<GuardException>(() => QuestionCount.From(-1));
    }

    [Fact]
    public void The_first_attempt_is_number_one_and_the_next_counts_up()
    {
        AttemptNumber.First.ShouldBe(AttemptNumber.From(1));
        AttemptNumber.First.Next().ShouldBe(AttemptNumber.From(2));
        (AttemptNumber.From(3) > AttemptNumber.From(2)).ShouldBeTrue();
    }

    [Fact]
    public void An_attempt_number_is_at_least_one() =>
        Should.Throw<GuardException>(() => AttemptNumber.From(0));

    [Fact]
    public void A_timestamp_is_kept_in_utc()
    {
        var timestamp = Timestamp.From(new DateTimeOffset(2027, 3, 1, 11, 0, 0, TimeSpan.FromHours(2)));

        timestamp.Value.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.ShouldBe(Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero)));
    }
}
