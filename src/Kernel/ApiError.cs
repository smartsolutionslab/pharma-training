using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Kernel;

/// <summary>
/// The base of every expected failure: a stable machine-readable code, a message for people, and the HTTP status it
/// maps to, so endpoints emit problem details without per-case mapping.
/// </summary>
public abstract record ApiError(string Code, string Message, HttpStatusCode Status)
{
    /// <summary>A domain rule's failure with the status Application gives it; the domain itself knows no HTTP.</summary>
    public static ApiError From(DomainError error, HttpStatusCode status)
    {
        Ensure.That(error).IsNotNull();
        return new DomainFailure(error, status);
    }

    /// <summary>A domain rule's refusal for now, not for good: 429, and the instant from which the caller may try again.</summary>
    public static ApiError RetryLater(DomainError error, DateTimeOffset retryAt)
    {
        Ensure.That(error).IsNotNull();
        return new RetryLaterFailure(error, retryAt);
    }
}
