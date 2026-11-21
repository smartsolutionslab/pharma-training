using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidPharmacyName()
    : ApiError("organisation.invalid-pharmacy-name", "Der Name einer Apotheke hat 1 bis 120 Zeichen und ist nicht leer.", HttpStatusCode.BadRequest);
