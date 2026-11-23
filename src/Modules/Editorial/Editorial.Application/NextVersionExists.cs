using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The version already has its successor; the editor continues that one instead of drafting another.</summary>
public sealed record NextVersionExists(Guid Version, Guid Existing, int Number)
    : ApiError(
        "editorial.next-version-exists",
        $"Version {Number} of this course already exists ({Existing}); continue that one instead of drafting another from {Version}.",
        HttpStatusCode.Conflict);
