using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>The model's answer to an AI test draft, in the shape its JSON schema asks for: untrusted until checked.</summary>
public sealed record TestDraftAnswer(IReadOnlyList<TestDraftAnswerQuestion> Questions)
{
    /// <summary>
    /// Checks the answer against the request and maps it onto drafted questions (ADR 0008): the count asked for, chapter
    /// numbers that were sent, texts the value objects take and at most <see cref="DraftedQuestion.MaxOptions"/> options.
    /// Too few options or the wrong correct ones are accepted for the editor to fix, and so is a split over the chapters
    /// other than their quotas. Any other defect fails the whole answer.
    /// </summary>
    public Result<IReadOnlyList<DraftedQuestion>, ApiError> ToDraftedQuestions(TestDraftRequest request)
    {
        Ensure.That(request).IsNotNull();

        if (Questions.Count != request.Questions.Value)
        {
            return Malformed(TestDraftDefect.WrongCount);
        }

        List<DraftedQuestion> drafted = [];
        foreach (var question in Questions)
        {
            if (question.Chapter < 1 || question.Chapter > request.Chapters.Count)
            {
                return Malformed(TestDraftDefect.UnknownChapter);
            }

            if (!QuestionText.TryFrom(question.Text, out var text))
            {
                return Malformed(TestDraftDefect.InvalidQuestionText);
            }

            List<(AnswerText Text, Correctness Correctness)> options = [];
            foreach (var option in question.Options)
            {
                if (!AnswerText.TryFrom(option.Text, out var answer))
                {
                    return Malformed(TestDraftDefect.InvalidOptionText);
                }

                options.Add((answer, option.Correct ? Correctness.Correct : Correctness.Incorrect));
            }

            if (options.Count > DraftedQuestion.MaxOptions)
            {
                return Malformed(TestDraftDefect.TooManyOptions);
            }

            var type = question.Type == TestDraftAnswerType.MultipleChoice ? QuestionType.MultipleChoice : QuestionType.SingleChoice;
            drafted.Add(new DraftedQuestion(text, type, request.Chapters[question.Chapter - 1].Id, options));
        }

        IReadOnlyList<DraftedQuestion> questions = drafted;
        return Success(questions);
    }

    private static Result<IReadOnlyList<DraftedQuestion>, ApiError> Malformed(TestDraftDefect defect) =>
        Failure(CourseVersionFailures.AiTestDraftMalformed(defect));
}
