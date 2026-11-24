using System.Diagnostics;
using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>
/// Maps <see cref="ProbeMappedTwice"/> twice, as a module's <c>*Failures</c> may: by its exact type to 409, and in the
/// generic method that callers passing a <see cref="DomainError"/> reach, to 400.
/// </summary>
public static class ProbeFailures
{
    public static ApiError From(ProbeMappedTwice error) => ApiError.From(error, HttpStatusCode.Conflict);

    public static ApiError From(DomainError error) => error switch
    {
        ProbeMappedTwice => ApiError.From(error, HttpStatusCode.BadRequest),
        _ => throw new UnreachableException(error.Code),
    };
}
