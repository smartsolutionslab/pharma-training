using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>How a caller's question and answer texts become values: <c>TryFrom</c> refuses exactly what <c>From</c> would.</summary>
public class QuestionValueObjectTests
{
    [Fact]
    public void A_question_or_answer_text_with_a_nul_character_is_refused_because_the_database_cannot_hold_it()
    {
        QuestionText.TryFrom("Wann\0 desinfizieren?", out _).ShouldBeFalse();
        AnswerText.TryFrom("Vor der\0 Rezeptur", out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => QuestionText.From("Wann\0 desinfizieren?"));
        Should.Throw<GuardException>(() => AnswerText.From("Vor der\0 Rezeptur"));
    }

    [Fact]
    public void A_callers_question_text_becomes_a_trimmed_text()
    {
        QuestionText.TryFrom("  Wann desinfizieren Sie die Hände? ", out var text).ShouldBeTrue();
        text.ShouldBe(QuestionText.From("Wann desinfizieren Sie die Hände?"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_question_text_is_no_text_and_no_exception(string? value)
    {
        QuestionText.TryFrom(value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_callers_question_text_is_no_text_exactly_when_from_refuses_it()
    {
        var padded = $" {new string('a', QuestionText.MaxLength)} ";

        QuestionText.TryFrom(new string('a', QuestionText.MaxLength), out _).ShouldBeTrue();
        QuestionText.TryFrom(new string('a', QuestionText.MaxLength + 1), out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => QuestionText.From(padded));
        QuestionText.TryFrom(padded, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_callers_answer_text_becomes_a_trimmed_text()
    {
        AnswerText.TryFrom(" Vor der Rezeptur  ", out var text).ShouldBeTrue();
        text.ShouldBe(AnswerText.From("Vor der Rezeptur"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_answer_text_is_no_text_and_no_exception(string? value)
    {
        AnswerText.TryFrom(value, out var text).ShouldBeFalse();
        text.ShouldBeNull();
    }

    [Fact]
    public void A_callers_answer_text_is_no_text_exactly_when_from_refuses_it()
    {
        var padded = $" {new string('a', AnswerText.MaxLength)} ";

        AnswerText.TryFrom(new string('a', AnswerText.MaxLength), out _).ShouldBeTrue();
        AnswerText.TryFrom(new string('a', AnswerText.MaxLength + 1), out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => AnswerText.From(padded));
        AnswerText.TryFrom(padded, out _).ShouldBeFalse();
    }
}
