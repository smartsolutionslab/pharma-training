using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidJobFunction()
    : ApiError("organisation.invalid-job-function", "Die Tätigkeit fehlt oder ist unbekannt.", HttpStatusCode.BadRequest);
