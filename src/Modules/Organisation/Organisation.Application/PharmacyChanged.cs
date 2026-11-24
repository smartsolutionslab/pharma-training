using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Another change to the pharmacy was saved after this one loaded it; nothing of this one was saved.</summary>
public sealed record PharmacyChanged(Guid Id)
    : ApiError("organisation.pharmacy-changed", "The pharmacy changed in the meantime; load it again and repeat the change.", HttpStatusCode.Conflict);
