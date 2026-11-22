using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// The module's settings from the host's configuration (ADR 0007). Four eyes is <c>Editorial:FourEyes</c>, off when
/// it is absent or blank; a value that is no boolean stops the host at startup, naming the key.
/// </summary>
public static class EditorialSettings
{
    public const string FourEyesKey = "Editorial:FourEyes";

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
}
