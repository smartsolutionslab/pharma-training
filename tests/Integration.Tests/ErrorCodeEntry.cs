namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>One row of web/app/src/i18n/error-codes.json: a code, its status and its English fallback message.</summary>
public sealed record ErrorCodeEntry(string Code, int Status, string Message);
