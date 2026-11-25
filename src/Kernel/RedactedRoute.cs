namespace SmartSolutionsLab.PharmaTraining.Kernel;

/// <summary>
/// Endpoint metadata for a route whose path carries a credential (#311): telemetry records the route's template as its
/// path, never the path it was called with. Add it where the route is mapped, <c>.WithMetadata(new RedactedRoute())</c>.
/// </summary>
public sealed class RedactedRoute;
