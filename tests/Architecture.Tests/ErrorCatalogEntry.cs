namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>One code a caller can receive, with the HTTP status it answers and its English message.</summary>
public sealed record ErrorCatalogEntry(string Code, int Status, string Message);
