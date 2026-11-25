using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The person whose erasure support requested is the last active manager of a pharmacy, so ending their membership would
/// leave it without one (#115). Nothing was changed; another member must be made manager there first.
/// </summary>
public sealed record ErasureRefusedLastManager()
    : ApiError(
        "organisation.erasure-refused-last-manager",
        "The person is the last active manager of a pharmacy. Make another member manager there first.",
        HttpStatusCode.Conflict);
