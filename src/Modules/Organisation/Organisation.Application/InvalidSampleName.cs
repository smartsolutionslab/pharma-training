using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

public sealed record InvalidSampleName()
    : ApiError("organisation.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
