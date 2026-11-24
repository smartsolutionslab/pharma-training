using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Telemetry;

/// <summary>Opens a <see cref="RequestLogScope"/> around every request, first in the pipeline, so every log within carries it.</summary>
public sealed class RequestLogScopeStartupFilter : IStartupFilter
{
    private const string Category = "SmartSolutionsLab.PharmaTraining.ServiceDefaults.Request";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        Ensure.That(next).IsNotNull();

        return app =>
        {
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
            app.Use(async (context, inner) =>
            {
                var scope = new RequestLogScope(context);
                using (logger.BeginScope(scope))
                {
                    try
                    {
                        await inner(context);
                    }
                    finally
                    {
                        scope.End();
                    }
                }
            });
            next(app);
        };
    }
}
