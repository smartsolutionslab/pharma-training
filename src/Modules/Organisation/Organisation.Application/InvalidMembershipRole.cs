using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidMembershipRole()
    : ApiError("organisation.invalid-membership-role", "The role is missing or unknown.", HttpStatusCode.BadRequest);
