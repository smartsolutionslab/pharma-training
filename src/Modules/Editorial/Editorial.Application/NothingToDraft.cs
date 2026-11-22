using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The approved questions already make up the whole test, so an AI draft has nothing left to add.</summary>
public sealed record NothingToDraft()
    : ApiError("editorial.nothing-to-draft", "The approved questions already fill the test's blueprint.", HttpStatusCode.Conflict);
