using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

public sealed record InvalidSampleName()
    : ApiError("learning.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
