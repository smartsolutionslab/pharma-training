using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// A blueprint's question count outside the range a request may ask for, with the code and message of the domain's
/// <see cref="TooFewQuestions"/>, taken from it so the two layers cannot differ.
/// </summary>
public sealed record InvalidQuestionCount()
    : ApiError(Refusal.Code, Refusal.Message, HttpStatusCode.BadRequest)
{
    /// <summary>The domain's refusal, read for its code and message only.</summary>
    private static readonly TooFewQuestions Refusal = new(QuestionCount.From(QuestionCount.Minimum));
}
