namespace SmartSolutionsLab.PharmaTraining.Edge.Forwarding;

/// <summary>
/// How much of a signed-in caller's body of unknown length the edge holds before it forwards it (docs/adr/0021), read
/// from the <c>Edge</c> section. A body above it is answered <c>413</c> by the edge. The default, 1 MiB, is sixteen
/// times the largest route cap (65 536 bytes); a browser sends a <c>Content-Length</c>, so only clients that stream a
/// body meet it.
/// </summary>
public sealed class BufferedBodyOptions
{
    public const string Section = "Edge";

    public long MaxBufferedRequestBodyBytes { get; set; } = 1_048_576;
}
