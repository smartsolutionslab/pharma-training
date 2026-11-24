using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Kernel;

/// <summary>
/// An <see cref="ApiError"/> made from a <see cref="DomainError"/> by <see cref="ApiError.RetryLater"/>: a refusal for
/// now, not for good, answered with 429, the instant from which the caller may try again and, when the rule knows it,
/// the instant it refused at, on the clock the first was computed on.
/// </summary>
public sealed record RetryLaterFailure : ApiError
{
    internal RetryLaterFailure(DomainError error, DateTimeOffset retryAt, Option<DateTimeOffset> refusedAt)
        : base(error.Code, error.Message, HttpStatusCode.TooManyRequests)
    {
        Error = error;
        RetryAt = retryAt;
        RefusedAt = refusedAt;
    }

    public DomainError Error { get; }

    /// <summary>The instant from which the refused request may be tried again.</summary>
    public DateTimeOffset RetryAt { get; }

    /// <summary>
    /// The instant the request was refused at, on the clock <see cref="RetryAt"/> was computed on, so the wait is
    /// counted from it rather than from the host's clock; none when the rule did not give it.
    /// </summary>
    public Option<DateTimeOffset> RefusedAt { get; }
}
