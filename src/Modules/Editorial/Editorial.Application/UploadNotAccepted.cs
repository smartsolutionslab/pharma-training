using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record UploadNotAccepted()
    : ApiError(
        "editorial.upload-not-accepted",
        "The uploaded file is not what its kind takes: a block blob of the kind's content type, put directly, not empty, at most the kind's size, and starting with the kind's own bytes (%PDF- for a PDF, ftyp at bytes 4 to 7 for a video).",
        HttpStatusCode.BadRequest);
