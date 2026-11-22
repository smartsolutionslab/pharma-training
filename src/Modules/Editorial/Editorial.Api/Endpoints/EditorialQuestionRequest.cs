using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The body that adds a question to a version or replaces one: the chapter it asks about, its text, its type and its
/// answer options in order. It carries no provenance: a question a person writes is a person's.
/// </summary>
public sealed record EditorialQuestionRequest(Guid ChapterId, string Text, EditorialQuestionType Type, IReadOnlyList<EditorialAnswerOptionRequest> Options);
