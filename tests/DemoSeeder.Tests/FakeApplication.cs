using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The application as the seeder sees it, behind one <see cref="HttpMessageHandler"/>: the edge (every module under
/// <c>/api</c>), Keycloak (sign-in and the admin API) and Mailpit, each at its own loopback origin. Every request is
/// recorded with the fake clock's time; a request no part has a route for answers 501, so the test names it.
/// </summary>
public sealed class FakeApplication : HttpMessageHandler
{
    public static readonly Uri EdgeUrl = new("http://localhost:5100");

    public static readonly Uri KeycloakUrl = new("http://localhost:8080");

    public static readonly Uri MailpitUrl = new("http://localhost:8025");

    public static readonly Uri WebOrigin = new("http://localhost:5173");

    private readonly Lock gate = new();

    private readonly List<RecordedRequest> requests = [];

    private readonly List<Failure> failures = [];

    private readonly FakeTimeProvider time;

    public FakeApplication(FakeTimeProvider time)
    {
        this.time = time;
        Identity = new FakeKeycloak();
        Mail = new FakeMailpit(time);
        Api = new FakeEdge(Identity, Mail, time);
    }

    public FakeKeycloak Identity { get; }

    public FakeMailpit Mail { get; }

    public FakeEdge Api { get; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>The requests to the edge, in order.</summary>
    public IReadOnlyList<RecordedRequest> EdgeRequests => [.. Requests.Where(request => request.Origin == "edge")];

    /// <summary>Forgets the requests recorded so far; the application's state stays.</summary>
    public void Forget()
    {
        lock (gate)
        {
            requests.Clear();
        }
    }

    /// <summary>
    /// The next request with the method whose path (the edge's without <c>/api</c>) matches the template, after
    /// <paramref name="after"/> such requests have been answered as usual, answers 500 once and changes nothing: a run
    /// that stops there.
    /// </summary>
    public void FailOnce(HttpMethod method, string template, int after = 0)
    {
        lock (gate)
        {
            failures.Add(new Failure(method.Method, template, after));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("A request without a URI.");
        var origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
        var cookie = request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : string.Empty;
        lock (gate)
        {
            var name = origin == EdgeUrl ? "edge" : origin == KeycloakUrl ? "keycloak" : origin == MailpitUrl ? "mailpit" : origin.ToString();
            var path = name == "edge" && uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ? uri.AbsolutePath[4..] : uri.AbsolutePath;
            var failure = failures.FirstOrDefault(each => each.Method == request.Method.Method && FakeRoute.TryMatch(each.Template, path, out _));
            FakeAnswer answer;
            if (failure is not null && failure.After == 0)
            {
                failures.Remove(failure);
                answer = new FakeAnswer(failure.Template, null, new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            else
            {
                if (failure is not null)
                {
                    failure.After--;
                }

                answer = name switch
                {
                    "edge" => Api.Handle(request, body),
                    "keycloak" => Identity.Handle(request, body, cookie),
                    "mailpit" => Mail.Handle(request),
                    _ => new FakeAnswer(string.Empty, null, FakeResponses.Unscripted(request)),
                };
            }

            requests.Add(new RecordedRequest(name, request.Method.Method, path, uri.Query, answer.Route, answer.User, body, cookie, time.GetUtcNow(), (int)answer.Response.StatusCode));
            return answer.Response;
        }
    }

    private sealed class Failure(string method, string template, int after)
    {
        public string Method { get; } = method;

        public string Template { get; } = template;

        public int After { get; set; } = after;
    }
}
