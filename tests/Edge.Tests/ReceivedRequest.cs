namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>A request the stub module service received from the edge: its method, path and query, and its bearer header.</summary>
public sealed record ReceivedRequest(string Method, string Path, string Query, string? Authorization);
