using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidEmail()
    : ApiError("organisation.invalid-email", "Die E-Mail-Adresse fehlt, ist zu lang oder ist keine.", HttpStatusCode.BadRequest);
