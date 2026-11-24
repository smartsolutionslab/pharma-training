using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Kernel.Tests;

/// <summary>
/// A domain rule that refuses for now, not for good, is mapped with <see cref="ApiError.RetryLater"/>: 429 with the
/// domain error's code and message, and the instant from which the caller may try again.
/// </summary>
public class RetryLaterFailureTests
{
    private static readonly DateTimeOffset RetryAt = new(2027, 4, 1, 8, 10, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RefusedAt = new(2027, 4, 1, 8, 4, 0, TimeSpan.Zero);

    [Fact]
    public void RetryLater_answers_429_with_the_domain_errors_code_message_and_instant()
    {
        var refused = new RenewedTooSoon();

        var error = ApiError.RetryLater(refused, RetryAt);

        error.Code.ShouldBe("billing.renewed-too-soon");
        error.Message.ShouldBe("Renewed a moment ago.");
        error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        var later = error.ShouldBeOfType<RetryLaterFailure>();
        later.Error.ShouldBe(refused);
        later.RetryAt.ShouldBe(RetryAt);
    }

    [Fact]
    public void RetryLater_refuses_a_missing_error() =>
        Should.Throw<GuardException>(() => ApiError.RetryLater(null!, RetryAt));

    // #294: the refusal carries the instant it was refused at, on the clock RetryAt was computed on.
    [Fact]
    public void A_refusal_with_the_instant_it_was_refused_at_carries_it()
    {
        var refused = new RenewedTooSoon();

        var error = ApiError.RetryLater(refused, RetryAt, RefusedAt);

        error.Code.ShouldBe("billing.renewed-too-soon");
        error.Message.ShouldBe("Renewed a moment ago.");
        error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        var later = error.ShouldBeOfType<RetryLaterFailure>();
        later.Error.ShouldBe(refused);
        later.RetryAt.ShouldBe(RetryAt);
        later.RefusedAt.ShouldBe(Option<DateTimeOffset>.Some(RefusedAt));
    }

    [Fact]
    public void A_refusal_without_it_carries_none()
    {
        var later = ApiError.RetryLater(new RenewedTooSoon(), RetryAt).ShouldBeOfType<RetryLaterFailure>();

        later.RefusedAt.ShouldBe(Option<DateTimeOffset>.None);
    }

    [Fact]
    public void RetryLater_with_the_instant_it_was_refused_at_refuses_a_missing_error() =>
        Should.Throw<GuardException>(() => ApiError.RetryLater(null!, RetryAt, RefusedAt));

    private sealed record RenewedTooSoon() : DomainError("billing.renewed-too-soon", "Renewed a moment ago.");
}
