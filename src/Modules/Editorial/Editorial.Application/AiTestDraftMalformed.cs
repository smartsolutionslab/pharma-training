using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The model's answer to an AI test draft broke the test's rules, so nothing was drafted: the upstream model failed,
/// not the caller's request. The message is the same whatever the <see cref="Defect"/>, which only tests and the evals read.
/// </summary>
public sealed record AiTestDraftMalformed(TestDraftDefect Defect)
    : ApiError("editorial.ai-draft-malformed", "The AI draft could not be used because the model's answer broke the test's rules. Nothing was changed; try again.", HttpStatusCode.BadGateway);
