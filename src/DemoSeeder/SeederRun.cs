using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// One run of the seeder: reads and guards the <c>DemoSeeder:*</c> settings, reads the edge's <c>/api/config.json</c> and
/// checks what it reports, then walks the plan through the edge, Keycloak and Mailpit, all through the one handler.
/// Answers the exit code: 0 seeded (or nothing to seed); 1 a step failed, whatever it threw, naming the step and never a
/// credential; 2 the settings or the edge's configuration were refused before any credential was sent.
/// </summary>
public static class SeederRun
{
    public const int Seeded = 0;

    public const int StepFailed = 1;

    public const int SettingsRefused = 2;

    public static async Task<int> RunAsync(
        IConfiguration configuration,
        SeedPlan plan,
        HttpMessageHandler handler,
        TimeProvider time,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(plan).IsNotNull();
        Ensure.That(handler).IsNotNull();
        Ensure.That(output).IsNotNull();

        if (!SeederSettings.TryRead(configuration, out var settings, out var refusal))
        {
            output.WriteLine(refusal);
            return SettingsRefused;
        }

        // The handler follows no redirect and keeps no cookie; the client sends every request once (POSTs are never retried).
        var steps = new StepTracker(handler);
        using var http = new HttpClient(steps);
        try
        {
            var edge = await EdgeClient.ConfigurationAsync(http, settings.EdgeUrl, cancellationToken);
            if (!settings.TryAccept(edge, out var realm, out refusal))
            {
                output.WriteLine(refusal);
                return SettingsRefused;
            }

            var created = await Seeder.SeedAsync(plan, settings, realm, http, time, output, cancellationToken);
            output.WriteLine($"Demo data: {created} created");
            return Seeded;
        }
        catch (SeedingException failure)
        {
            output.WriteLine($"Demo data: a step failed: {failure.Message}");
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Whatever else a step threw ends the run as a failed step, never with the runtime's 134: named by the last
            // request and the kind of failure, never by its message, which could repeat an answer's content.
            output.WriteLine($"Demo data: a step failed at {steps.Current}: {Kind(failure)}.");
        }

        return StepFailed;
    }

    private static string Kind(Exception failure) => failure switch
    {
        HttpRequestException request => $"the request did not reach its server ({request.HttpRequestError})",
        OperationCanceledException => "the request timed out",
        JsonException => "an answer was not the JSON it should be",
        _ => failure.GetType().Name,
    };
}
