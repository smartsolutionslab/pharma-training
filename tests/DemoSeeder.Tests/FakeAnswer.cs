namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>How a part of the fake answered a request: the route template it matched, the user it served, the response.</summary>
public sealed record FakeAnswer(string Route, string? User, HttpResponseMessage Response);
