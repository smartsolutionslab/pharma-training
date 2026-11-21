using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidSampleName()
    : ApiError("editorial.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
