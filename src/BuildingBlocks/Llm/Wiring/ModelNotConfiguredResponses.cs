using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class ModelNotConfiguredResponses
{
    /// <summary>
    /// A request that needs the model while its endpoint or key is not set answers <c>503</c> problem details with the
    /// code of <see cref="ModelNotConfigured"/>; the rest of the application keeps working.
    /// </summary>
    public static IServiceCollection AddModelNotConfiguredResponses(this IServiceCollection services) =>
        services.AddProblemDetails()
            .AddExceptionHandler<ModelNotConfiguredExceptionHandler>();

    /// <summary>
    /// The backstop for an application run without the model's settings, as a developer without a key runs it. The
    /// failure comes from resolving the model's client, before any handler runs, and is the infrastructure's, not the
    /// caller's: it answers <c>503</c> instead of <c>500</c>, and names no setting's value.
    /// </summary>
    private sealed partial class ModelNotConfiguredExceptionHandler(IProblemDetailsService problemDetails, ILogger<ModelNotConfiguredExceptionHandler> logger) : IExceptionHandler
    {
        /// <summary>The code, title and status of every problem a missing model answers.</summary>
        private static readonly ModelNotConfigured Refusal = new();

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            Ensure.That(httpContext).IsNotNull();

            if (Missing(exception) is not { } missing)
            {
                return false;
            }

            LogNotConfigured(logger, missing.Parameter);

            httpContext.Response.StatusCode = (int)Refusal.Status;

            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails =
                {
                    Status = (int)Refusal.Status,
                    Title = Refusal.Message,
                    Detail = "AI actions are unavailable until the application's model is configured; everything else keeps working.",
                    Extensions = { ["code"] = Refusal.Code },
                },
            });

            return true;
        }

        /// <summary>The container may wrap the exception a factory throws; it is found wherever it sits in the chain.</summary>
        private static ModelNotConfiguredException? Missing(Exception? exception) => exception switch
        {
            null => null,
            ModelNotConfiguredException missing => missing,
            AggregateException aggregate => aggregate.InnerExceptions.Select(Missing).FirstOrDefault(found => found is not null),
            _ => Missing(exception.InnerException),
        };

        [LoggerMessage(Level = LogLevel.Warning, Message = "The model is not configured: the AppHost parameter '{Parameter}' has no value; answered 503")]
        private static partial void LogNotConfigured(ILogger logger, string parameter);
    }
}
