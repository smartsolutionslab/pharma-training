using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// What every answer of the edge tells the browser (the security review of #114, docs/adr/0005): never guess a type other
/// than the one named (<c>X-Content-Type-Options: nosniff</c>), and send another origin no path
/// (<c>Referrer-Policy: strict-origin-when-cross-origin</c>). Outside Development, where the edge is deployed and reached
/// over HTTPS alone through the Container Apps ingress, also use HTTPS alone for a year
/// (<c>Strict-Transport-Security</c>; a browser ignores it over plain HTTP). They are set as the answer starts, so they
/// cover the edge's own answers, its files, its errors and what it forwards from a service alike, and replace a service's
/// own. <c>index.html</c>'s Content-Security-Policy is <see cref="SpaHosting"/>'s. Placed first.
/// </summary>
public static class SecurityHeaders
{
    private const string Nosniff = "nosniff";

    private const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>A year, the edge's host and every name under it.</summary>
    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        var httpsAlone = !app.Environment.IsDevelopment();
        app.Use((context, next) =>
        {
            var response = context.Response;
            response.OnStarting(() =>
            {
                response.Headers.XContentTypeOptions = Nosniff;
                response.Headers["Referrer-Policy"] = ReferrerPolicy;
                if (httpsAlone)
                {
                    response.Headers.StrictTransportSecurity = StrictTransportSecurity;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
        return app;
    }
}
