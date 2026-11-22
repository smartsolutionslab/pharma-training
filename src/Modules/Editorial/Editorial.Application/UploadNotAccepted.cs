using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record UploadNotAccepted()
    : ApiError(
        "editorial.upload-not-accepted",
        "The file is not a PDF or MP4 video.",
        HttpStatusCode.BadRequest);
