using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// A question id no question can have, such as the empty Guid: not found, with the code and message of the domain's
/// <see cref="QuestionNotFound"/>, taken from it so the two cannot differ.
/// </summary>
public sealed record UnknownQuestion(Guid Version, Guid Question)
    : ApiError(Refusal.Code, Refusal.Message, HttpStatusCode.NotFound)
{
    /// <summary>The domain's refusal, read for its code and message only.</summary>
    private static readonly QuestionNotFound Refusal = new(CourseVersionId.New(), QuestionId.New());
}
