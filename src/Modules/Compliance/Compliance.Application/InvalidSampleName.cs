using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

public sealed record InvalidSampleName()
    : ApiError("compliance.invalid-sample-name", $"A sample's name is 1 to {SampleName.MaxLength} characters and not blank.", HttpStatusCode.BadRequest);
