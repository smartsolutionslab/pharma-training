using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

/// <summary>
/// An examination as its learner's client shows it, from the aggregate and the version's copy. The copy holds the
/// solutions; only the questions' and options' ids, texts and types leave it, never which option is right, a
/// question's chapter or a verdict per question.
/// </summary>
public static class ExaminationMappings
{
    public static AssessmentExaminationDto ToAssessmentExaminationDto(this Examination examination, Option<TestDefinitionCopy> copy)
    {
        Ensure.That(examination).IsNotNull();

        var current = examination.CurrentAttempt;
        var last = examination.Attempts.LastOrDefault(attempt => attempt.Outcome is not null);
        return new AssessmentExaminationDto(
            examination.Id.Value,
            examination.Session.Value,
            examination.Course.Value,
            examination.CourseVersion.Value,
            ToStatus(examination.Status),
            examination.AttemptsSinceUnlock.Count,
            copy.HasValue ? copy.Value.MaxAttempts : null,
            copy.HasValue && current.HasValue ? current.Value.ToAssessmentAttemptDto(copy.Value) : null,
            copy.HasValue && last is not null ? examination.ToAssessmentAttemptResultDto(last, copy.Value) : null);
    }

    /// <summary>The attempt's questions in its stored order, each question's options in the order derived from the attempt's id (ADR 0022).</summary>
    public static AssessmentAttemptDto ToAssessmentAttemptDto(this TestAttempt attempt, TestDefinitionCopy copy)
    {
        Ensure.That(attempt).IsNotNull();
        Ensure.That(copy).IsNotNull();

        var questions = copy.Questions.ToDictionary(question => question.QuestionId);
        return new AssessmentAttemptDto(
            attempt.Id.Value,
            attempt.Number.Value,
            [.. attempt.Questions.Select(question => ToQuestionDto(attempt, questions[question.Value]))]);
    }

    /// <summary>
    /// A submitted attempt's result. The chapters to repeat are every chapter failed since the unlock once the
    /// examination is exhausted, the attempt's own failed chapters when it failed otherwise, and none when it passed.
    /// </summary>
    public static AssessmentAttemptResultDto ToAssessmentAttemptResultDto(this Examination examination, TestAttempt attempt, TestDefinitionCopy copy)
    {
        Ensure.That(examination).IsNotNull();
        Ensure.That(attempt).IsNotNull();
        Ensure.That(copy).IsNotNull();

        var passed = attempt.Outcome == AttemptOutcome.Passed;
        var toRepeat = passed
            ? []
            : examination.Status == ExaminationStatus.Exhausted ? examination.ChaptersToRelearn : attempt.FailedChapters;
        return new AssessmentAttemptResultDto(
            attempt.Id.Value,
            attempt.Number.Value,
            attempt.Score?.Value ?? 0,
            copy.PassingScorePercent,
            passed ? AssessmentAttemptOutcome.Passed : AssessmentAttemptOutcome.Failed,
            [.. toRepeat.Select(chapter => chapter.Value)],
            AttemptsLeft(examination, copy));
    }

    private static int AttemptsLeft(Examination examination, TestDefinitionCopy copy) =>
        examination.Status is ExaminationStatus.Unlocked or ExaminationStatus.InAttempt
            ? Math.Max(0, copy.MaxAttempts - examination.AttemptsSinceUnlock.Count)
            : 0;

    private static AssessmentAttemptQuestionDto ToQuestionDto(TestAttempt attempt, TestQuestionCopy question)
    {
        var texts = question.Options.ToDictionary(option => option.OptionId, option => option.Text);
        var options = AttemptShuffle.Options(attempt.Id, question.Options.Select(option => AnswerOptionId.From(option.OptionId)));
        var chosen = attempt.Answers.TryGetValue(QuestionVersionId.From(question.QuestionId), out var answer) ? answer : [];
        return new AssessmentAttemptQuestionDto(
            question.QuestionId,
            ToType(question.Type),
            question.Text,
            [.. options.Select(option => new AssessmentAnswerOptionDto(option.Value, texts[option.Value]))],
            [.. chosen.Select(option => option.Value)]);
    }

    private static AssessmentExaminationStatus ToStatus(ExaminationStatus status) => status switch
    {
        ExaminationStatus.Locked => AssessmentExaminationStatus.Locked,
        ExaminationStatus.Unlocked => AssessmentExaminationStatus.Unlocked,
        ExaminationStatus.InAttempt => AssessmentExaminationStatus.InAttempt,
        ExaminationStatus.Passed => AssessmentExaminationStatus.Passed,
        ExaminationStatus.Exhausted => AssessmentExaminationStatus.Exhausted,
        _ => throw new InvalidOperationException($"Examination status {status} has no counterpart on the wire."),
    };

    private static AssessmentQuestionType ToType(TestQuestionType type) => type switch
    {
        TestQuestionType.SingleChoice => AssessmentQuestionType.SingleChoice,
        TestQuestionType.MultipleChoice => AssessmentQuestionType.MultipleChoice,
        _ => throw new InvalidOperationException($"Question type {type} has no counterpart on the wire."),
    };
}
