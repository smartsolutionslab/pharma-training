using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidUploadKind()
    : ApiError("editorial.invalid-upload-kind", "An upload is for a chapter of kind Pdf or Video.", HttpStatusCode.BadRequest);
