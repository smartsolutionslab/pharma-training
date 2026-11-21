using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record PharmacyNotFound(Guid Id)
    : ApiError("organisation.pharmacy-not-found", $"Es gibt keine Apotheke {Id}, in der Sie Mitglied sind.", HttpStatusCode.NotFound);
