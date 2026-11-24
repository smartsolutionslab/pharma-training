using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record PharmacyNotFound(Guid Id)
    : ApiError("organisation.pharmacy-not-found", "There is no pharmacy of yours with this id.", HttpStatusCode.NotFound);
