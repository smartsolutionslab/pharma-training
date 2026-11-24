using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// The module's settings from the host's configuration (ADR 0007). Four eyes is <c>Editorial:FourEyes</c>, off when
/// it is absent or blank; a value that is no boolean stops the host at startup, naming the key. The bounds on reading a
/// PDF's text (#316) are <c>Editorial:PdfText:MaxConcurrentParses</c> and <c>Editorial:PdfText:TimeoutSeconds</c>, each
/// its default when absent or blank; a value out of its range stops the host the same way.
/// </summary>
public static class EditorialSettings
{
    public const string FourEyesKey = "Editorial:FourEyes";

    public const string MaxConcurrentParsesKey = "Editorial:PdfText:MaxConcurrentParses";

    public const string TimeoutSecondsKey = "Editorial:PdfText:TimeoutSeconds";

    public static ReviewSettings ReviewFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var value = configuration[FourEyesKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return ReviewSettings.Default;
        }

        if (!bool.TryParse(value, out var fourEyes))
        {
            throw new InvalidOperationException($"The setting {FourEyesKey} is '{value}'; it accepts true or false, or nothing for false.");
        }

        if (fourEyes)
        {
            return new ReviewSettings(ReviewPolicy.FourEyes);
        }

        return ReviewSettings.Default;
    }

    public static PdfTextLimits PdfTextFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var parses = WholeNumberAt(configuration, MaxConcurrentParsesKey, PdfTextLimits.FewestParses, PdfTextLimits.MostParses, PdfTextLimits.DefaultParses);
        var seconds = WholeNumberAt(configuration, TimeoutSecondsKey, PdfTextLimits.ShortestTimeoutSeconds, PdfTextLimits.LongestTimeoutSeconds, PdfTextLimits.DefaultTimeoutSeconds);
        return PdfTextLimits.Of(parses, TimeSpan.FromSeconds(seconds));
    }

    private static int WholeNumberAt(IConfiguration configuration, string key, int minimum, int maximum, int fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
        {
            throw new InvalidOperationException($"The setting {key} is '{value}'; it accepts a whole number from {minimum} to {maximum}, or nothing for {fallback}.");
        }

        return number;
    }
}
