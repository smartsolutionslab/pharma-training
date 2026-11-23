using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;

/// <summary>
/// The endpoint filter <c>RequireUserRateLimit</c> adds: it runs after authorization, counts the caller's request under
/// the limit and, over it, answers 429 with <c>Retry-After</c> and the limit's refusal as problem details, without
/// invoking the endpoint (docs/adr/0020).
/// </summary>
internal sealed partial class UserRateLimitFilter : IEndpointFilter
{
    private readonly UserRateLimit limit;
    private readonly UserRateLimiter limiter;
    private readonly ILogger<UserRateLimitFilter> logger;

    public UserRateLimitFilter(UserRateLimit limit, UserRateLimiter limiter, ILogger<UserRateLimitFilter> logger)
    {
        Ensure.That(limit).IsNotNull();
        Ensure.That(limiter).IsNotNull();
        Ensure.That(logger).IsNotNull();
        this.limit = limit;
        this.limiter = limiter;
        this.logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        Ensure.That(context).IsNotNull();
        Ensure.That(next).IsNotNull();

        var subject = UserRateSubject.Of(context.HttpContext.User);
        var decision = limiter.TryAcquire(limit, subject);
        if (decision.IsAdmitted)
        {
            return await next(context);
        }

        var seconds = Math.Max(1, (long)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        LogRefused(subject, limit.Name, decision.RetryAfter);
        return TypedResults.Problem(
            title: limit.Refusal.Message,
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = limit.Refusal.Code });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The caller {Subject} reached the limit {Limit}; refused for {RetryAfter}")]
    private partial void LogRefused(string subject, string limit, TimeSpan retryAfter);
}
