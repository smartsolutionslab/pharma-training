using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidEmail()
    : ApiError("organisation.invalid-email", "The e-mail address is missing, too long or not an e-mail address.", HttpStatusCode.BadRequest);
