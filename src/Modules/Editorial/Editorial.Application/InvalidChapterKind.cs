using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

public sealed record InvalidChapterKind()
    : ApiError("editorial.invalid-chapter-kind", "A chapter is of kind Text, Pdf or Video; without a kind it is Text.", HttpStatusCode.BadRequest);
