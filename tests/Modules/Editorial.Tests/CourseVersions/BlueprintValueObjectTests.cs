using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>How a caller's blueprint values become values: <c>TryFrom</c> accepts exactly what <c>From</c> would, and throws for none.</summary>
public class BlueprintValueObjectTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void A_question_count_is_accepted_exactly_in_its_range(int value, bool accepted)
    {
        QuestionCount.TryFrom(value, out var result).ShouldBe(accepted);

        if (accepted)
        {
            result.ShouldBe(QuestionCount.From(value));
        }
        else
        {
            result.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(80, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void A_score_percent_is_accepted_exactly_in_its_range(int value, bool accepted)
    {
        ScorePercent.TryFrom(value, out var result).ShouldBe(accepted);

        if (accepted)
        {
            result.ShouldBe(ScorePercent.From(value));
        }
        else
        {
            result.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void A_recurrence_interval_is_accepted_exactly_in_its_range(int value, bool accepted)
    {
        RecurrenceInterval.TryFrom(value, out var result).ShouldBe(accepted);

        if (accepted)
        {
            result.ShouldBe(RecurrenceInterval.From(value));
        }
        else
        {
            result.ShouldBeNull();
        }
    }

    [Fact]
    public void The_ranges_are_named()
    {
        QuestionCount.Minimum.ShouldBe(0);
        QuestionCount.Maximum.ShouldBe(50);
        ScorePercent.Minimum.ShouldBe(0);
        ScorePercent.Maximum.ShouldBe(100);
        RecurrenceInterval.MinimumMonths.ShouldBe(1);
        RecurrenceInterval.MaximumMonths.ShouldBe(60);
        TestBlueprint.MinimumQuestions.ShouldBe(1);
    }
}
