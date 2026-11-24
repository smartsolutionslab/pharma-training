using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Http;

public static class ResultHttpExtensions
{
    /// <summary>The problem details extension holding the instant a refusal to retry later may be retried.</summary>
    public const string RetryAtExtension = "retryAt";

    /// <summary>200 with the value, or problem details carrying the error's status and code.</summary>
    /// <remarks>The typed union tells the OpenAPI document what a success returns, so clients are generated with its type.</remarks>
    public static Results<Ok<TValue>, ProblemHttpResult> ToHttpResult<TValue, TError>(this Result<TValue, TError> result)
        where TValue : notnull
        where TError : ApiError =>
        result.Match<Results<Ok<TValue>, ProblemHttpResult>>(
            value => TypedResults.Ok(value),
            error => ProblemOf(error));

    /// <summary>204 without a body, or problem details carrying the error's status and code: for a change that answers nothing.</summary>
    public static Results<NoContent, ProblemHttpResult> ToNoContentHttpResult<TError>(this Result<Unit, TError> result)
        where TError : ApiError =>
        result.Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => TypedResults.Problem(
                title: error.Message,
                statusCode: (int)error.Status,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code }));

    /// <summary>201 with the value and its location, or problem details carrying the error's status and code: for a create.</summary>
    public static Results<Created<TValue>, ProblemHttpResult> ToCreatedHttpResult<TValue, TError>(this Result<TValue, TError> result, Func<TValue, string> location)
        where TValue : notnull
        where TError : ApiError
    {
        Ensure.That(location).IsNotNull();

        return result.Match<Results<Created<TValue>, ProblemHttpResult>>(
            value => TypedResults.Created(location(value), value),
            error => ProblemOf(error));
    }

    /// <summary>
    /// 201 with the value and its location when this call created it, 200 with the value when it found one already
    /// there, or problem details carrying the error's status and code: for a create that is idempotent.
    /// </summary>
    public static Results<Created<TValue>, Ok<TValue>, ProblemHttpResult> ToCreatedOrOkHttpResult<TValue, TError>(this Result<TValue, TError> result, bool created, Func<TValue, string> location)
        where TValue : notnull
        where TError : ApiError
    {
        Ensure.That(location).IsNotNull();

        return result.Match<Results<Created<TValue>, Ok<TValue>, ProblemHttpResult>>(
            value => created ? TypedResults.Created(location(value), value) : TypedResults.Ok(value),
            error => ProblemOf(error));
    }

    /// <summary>
    /// The value's bytes written straight into the response body by <paramref name="stream"/>, or problem details carrying
    /// the error's status and code: for a download the service assembles as it sends it.
    /// </summary>
    public static Results<PushStreamHttpResult, ProblemHttpResult> ToStreamHttpResult<TValue, TError>(this Result<TValue, TError> result, Func<TValue, PushStreamHttpResult> stream)
        where TValue : notnull
        where TError : ApiError
    {
        Ensure.That(stream).IsNotNull();

        return result.Match<Results<PushStreamHttpResult, ProblemHttpResult>>(
            value => stream(value),
            error => ProblemOf(error));
    }

    /// <summary>
    /// Problem details carrying the error's status and code, and for a refusal to retry later
    /// (<see cref="RetryLaterFailure"/>) the instant as <c>retryAt</c>, from which the module's route group sets <c>Retry-After</c>.
    /// </summary>
    private static ProblemHttpResult ProblemOf(ApiError error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error is RetryLaterFailure later)
        {
            extensions[RetryAtExtension] = later.RetryAt;
        }

        return TypedResults.Problem(title: error.Message, statusCode: (int)error.Status, extensions: extensions);
    }
}
