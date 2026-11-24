using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The version already has its successor; the editor continues that one instead of drafting another.</summary>
public sealed record NextVersionExists()
    : ApiError(
        "editorial.next-version-exists",
        "The next version of this course already exists; continue that one instead of drafting another.",
        HttpStatusCode.Conflict);
