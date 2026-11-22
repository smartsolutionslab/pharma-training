using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>Nothing was uploaded under the name yet; once the upload's PUT has succeeded, the same change succeeds.</summary>
public sealed record UploadNotFound()
    : ApiError("editorial.upload-not-found", "Nothing was uploaded under this file's name yet; upload it, then repeat the change.", HttpStatusCode.Conflict);
