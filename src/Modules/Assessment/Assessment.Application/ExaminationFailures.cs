using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>Everything the examination's use cases refuse, each with its own code.</summary>
public static class ExaminationFailures
{
    /// <summary>No examination of the caller's by this id or session; another person's is answered alike.</summary>
    public static ApiError NotFound(Guid id) => new ExaminationNotFound(id);

    public static ApiError IdentityIncomplete() => new IdentityIncomplete();

    /// <summary>A write that lost the append race on the examination's stream; nothing of it was stored or announced.</summary>
    public static ApiError Changed(Guid id) => new ExaminationChanged(id);

    public static ApiError TestDefinitionMissing(Guid courseVersionId) => new TestDefinitionMissing(courseVersionId);

    public static ApiError OptionNotInQuestion(Guid questionId, Guid optionId) => new OptionNotInQuestion(questionId, optionId);

    public static ApiError SingleChoiceTakesOneOption(Guid questionId) => new SingleChoiceTakesOneOption(questionId);

    /// <summary>
    /// The examination's own refusals: a step its state does not allow conflicts with it, and a question outside the
    /// open attempt is not found. The handlers load the definition by the examination's own version, so a definition
    /// of another version is a bug: a 500 without details. The union is closed and the switch has no discard arm, so a
    /// new case fails the build until it is mapped here.
    /// </summary>
    public static ApiError From(ExaminationError error) => error switch
    {
        ExaminationNotUnlocked refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        NoOpenAttempt refusal => ApiError.From(refusal, HttpStatusCode.Conflict),
        QuestionNotInAttempt refusal => ApiError.From(refusal, HttpStatusCode.NotFound),
        DefinitionOfAnotherVersion refusal => throw new InvalidOperationException(
            $"The examination {refusal.Examination} was scored against the definition of course version {refusal.CourseVersion}, not its own."),
    };
}
