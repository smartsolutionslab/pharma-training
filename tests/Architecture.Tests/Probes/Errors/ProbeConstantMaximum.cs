using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;

/// <summary>An error whose message interpolates a compile-time constant, not an argument: every rule lets it pass.</summary>
public sealed record ProbeConstantMaximum() : ApiError("probe.constant-maximum", $"Ask for at most {Maximum} probes.", HttpStatusCode.BadRequest)
{
    public const int Maximum = 3;
}
