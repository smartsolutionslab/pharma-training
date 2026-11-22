using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record UploadNotAccepted()
    : ApiError(
        "editorial.upload-not-accepted",
        "The uploaded file is not what its kind takes: a block blob of the kind's content type, not empty and at most the kind's size.",
        HttpStatusCode.BadRequest);
