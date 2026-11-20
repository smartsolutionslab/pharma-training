using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

public sealed record InvalidSampleName()
    : ApiError("catalog.invalid-sample-name", $"A sample's name is 1 to {Sample.MaxNameLength} characters and not blank.", HttpStatusCode.BadRequest);
