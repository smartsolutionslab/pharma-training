using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The chapters with text together are longer than one AI draft request may send (<see cref="TestDraftRequest.MaxCharacters"/>).</summary>
public sealed record TooMuchContentToDraft()
    : ApiError("editorial.too-much-content-to-draft", "The version's chapters are too long to draft a test from in one request.", HttpStatusCode.UnprocessableEntity);
