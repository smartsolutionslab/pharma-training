using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Trace;
using SmartSolutionsLab.PharmaTraining.ServiceDefaults.Telemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>Service discovery, resilience, health checks and OpenTelemetry for every service Aspire runs.</summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>ASP.NET Core hosting's category; its request scope holds the request's path, which may carry a credential.</summary>
    private const string HostingDiagnostics = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>The name Microsoft.Extensions.AI's UseOpenTelemetry gives the model's traces and metrics by default.</summary>
    private const string ModelTelemetry = "Experimental.Microsoft.Extensions.AI";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly);
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // A request's path may carry a credential (Certification's verification code, #311), and scopes are exported:
        // hosting's request scope, which holds the path, is never opened (it is opened only while its category logs), and
        // every request carries RequestLogScope instead, whose RequestPath is the route's template.
        builder.Logging.AddFilter(HostingDiagnostics, LogLevel.None);
        builder.Services.AddTransient<IStartupFilter, RequestLogScopeStartupFilter>();

        // A route marked RedactedRoute records its template as url.path, never the path it was called with (#311).
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                if (response.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RedactedRoute>() is not null
                    && RequestPaths.Template(response.HttpContext) is { } template)
                {
                    activity.SetTag("url.path", template);
                }
            });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ModelTelemetry))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(ModelTelemetry)
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        // Health endpoints outside Development need a deliberate decision: https://aka.ms/aspire/healthchecks
        if (app.Environment.IsDevelopment())
        {
            // Probed without a token, by Aspire and by orchestrators, so anonymous where they are mapped.
            app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") }).AllowAnonymous();
        }

        return app;
    }
}
