using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidPharmacyName()
    : ApiError("organisation.invalid-pharmacy-name", $"A pharmacy's name is 1 to {PharmacyName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
