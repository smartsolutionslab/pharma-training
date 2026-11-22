namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>One answer option of a question, and whether it is correct.</summary>
public sealed record EditorialAnswerOptionDto(Guid Id, string Text, bool Correct);
