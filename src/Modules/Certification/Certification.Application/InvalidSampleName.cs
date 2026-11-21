using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

public sealed record InvalidSampleName()
    : ApiError("certification.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
