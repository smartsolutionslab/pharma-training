using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidVideoDuration()
    : ApiError("editorial.invalid-video-duration", $"A video chapter lasts 1 to {VideoDuration.MaxSeconds} seconds.", HttpStatusCode.BadRequest);
