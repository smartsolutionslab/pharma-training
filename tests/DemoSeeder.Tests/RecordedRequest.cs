namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// A request the fake answered: where it went (edge, keycloak or mailpit), its method, path and query, the route template
/// that matched it (the edge's without <c>/api</c>), the signed-in user it carried, its body, its cookies, the fake
/// clock's time when it arrived and the status it was answered with.
/// </summary>
public sealed record RecordedRequest(
    string Origin,
    string Method,
    string Path,
    string Query,
    string Route,
    string? User,
    string Body,
    string Cookie,
    DateTimeOffset At,
    int Status)
{
    public bool IsWrite => Method is "POST" or "PUT" or "PATCH" or "DELETE";

    public override string ToString() => $"{Method} {Origin} {Path}{Query} as {User ?? "-"} -> {Status}";
}
