using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidChapterOrder()
    : ApiError("editorial.invalid-chapter-order", "The order names every chapter of the version by its id, none of them empty.", HttpStatusCode.BadRequest);
