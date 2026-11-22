namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>One answer option of a question as a caller sends it: its text and whether it is correct.</summary>
public sealed record EditorialAnswerOptionRequest(string Text, bool Correct);
