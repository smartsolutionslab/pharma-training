using System.Buffers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;

namespace SmartSolutionsLab.PharmaTraining.Edge.Forwarding;

/// <summary>
/// Sends every forwarded body behind <c>Expect: 100-continue</c> (#214, docs/adr/0021), so a service refuses a body
/// too large before the edge sends a byte of it. A body of unknown length from a caller the edge has signed in is read
/// first, up to <see cref="BufferedBodyOptions.MaxBufferedRequestBodyBytes"/>, and forwarded with its length; one
/// above that is answered <c>413</c> here. An anonymous caller's body is streamed as it comes: holding it would let
/// anyone fill the edge's disk before a service answers <c>401</c>. A response set here means YARP does not proxy.
/// </summary>
public sealed class ContinueExpectingRequestTransform(IOptions<BufferedBodyOptions> options) : RequestTransform
{
    // EnableBuffering's own default: a body up to this size is held in memory, a larger one in a temporary file.
    private const int MemoryThreshold = 30_720;

    private const int ReadSize = 16_384;

    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        Ensure.That(context).IsNotNull();

        var content = context.ProxyRequest.Content;
        if (content is null)
        {
            return;
        }

        var http = context.HttpContext;
        if (content.Headers.ContentLength is null && http.User.Identity?.IsAuthenticated is true)
        {
            try
            {
                var length = await HoldAsync(http.Request, options.Value.MaxBufferedRequestBodyBytes, context.CancellationToken);
                if (length is null)
                {
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                content.Headers.ContentLength = length;
            }
            catch (BadHttpRequestException refusal)
            {
                // Kestrel refused the caller's body while the edge read it: over the edge's own cap (413) or with
                // broken chunk framing (400). Its status is the answer, not the 502 a throwing transform would get.
                http.Response.StatusCode = refusal.StatusCode;
                return;
            }
        }

        context.ProxyRequest.Headers.ExpectContinue = true;
    }

    /// <summary>Reads the body into the request's buffer, at most one byte past the limit: its length, or null above the limit.</summary>
    private static async Task<long?> HoldAsync(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        request.EnableBuffering((int)Math.Min(MemoryThreshold, limit), limit + 1);
        var body = request.Body;
        var buffer = ArrayPool<byte>.Shared.Rent(ReadSize);
        try
        {
            var length = 0L;
            while (length <= limit)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(ReadSize, limit - length + 1)), cancellationToken);
                if (read == 0)
                {
                    body.Position = 0;
                    return length;
                }

                length += read;
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
