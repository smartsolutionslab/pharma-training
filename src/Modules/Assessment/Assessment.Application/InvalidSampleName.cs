using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

public sealed record InvalidSampleName()
    : ApiError("assessment.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
