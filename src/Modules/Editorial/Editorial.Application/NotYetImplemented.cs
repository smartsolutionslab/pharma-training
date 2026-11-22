using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// Red-test stub for #15 (task A3): what every stubbed use case and failure of #15 answers until B4 and B5 implement
/// them, so their tests fail on an assertion rather than on a missing type. B5 deletes it.
/// </summary>
public sealed record NotYetImplemented()
    : ApiError("editorial.not-yet-implemented", "Issue #15 is not implemented yet.", HttpStatusCode.NotImplemented);
