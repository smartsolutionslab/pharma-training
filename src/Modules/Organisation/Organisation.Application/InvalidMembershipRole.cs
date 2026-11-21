using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidMembershipRole()
    : ApiError("organisation.invalid-membership-role", "Die Rolle fehlt oder ist unbekannt.", HttpStatusCode.BadRequest);
