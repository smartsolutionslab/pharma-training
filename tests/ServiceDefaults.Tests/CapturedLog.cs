namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Tests;

/// <summary>A log entry as a provider that reads scopes saw it: its message and every scope value as <c>name=value</c>.</summary>
public sealed record CapturedLog(string Message, IReadOnlyList<string> Scopes);
