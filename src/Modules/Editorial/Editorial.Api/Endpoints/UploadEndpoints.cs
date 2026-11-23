using System.Text;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor asks where the browser uploads a draft's PDF or video: 200 with a short-lived URL, since nothing of this
/// API is created; the chapter then names the upload's blob as its file.
/// </summary>
public sealed class UploadEndpoints : IEndpoint
{
    /// <summary>
    /// The most bytes the route reads of a body. The body is read whole into a string before it is parsed, and a request
    /// is a few bytes of JSON, so a larger one is refused by the server (413) rather than read into memory.
    /// </summary>
    private const int UploadRequestLimit = 4096;

    /// <summary>
    /// The charset of a body that names none, JSON's own, refusing invalid bytes rather than replacing them. Its preamble
    /// is the UTF-8 byte-order mark, so the reader skips one that leads the body instead of handing it to the parser.
    /// </summary>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        // The body is read here rather than bound, so that a kind the converter cannot read (Text, which takes no file)
        // is refused with the module's code like a missing or undefined one, instead of the framework's bare 400. The
        // document describes it as EditorialUploadRequest, whose kind names exactly what is accepted: Pdf or Video.
        endpoints.MapPost("/course-versions/{id:guid}/uploads", async (Guid id, HttpRequest http, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var request = await ReadAsync(http, cancellationToken);
            if (request is not { Kind: var kind } || !Enum.IsDefined(kind))
            {
                return Result<EditorialChapterUploadDto, ApiError>.Failure(CourseVersionFailures.InvalidUploadKind()).ToHttpResult();
            }

            return (await dispatcher.QueryAsync(new RequestChapterUpload(id, kind.ToEditorialChapterKind()), cancellationToken)).ToHttpResult();
        })
        .Accepts<EditorialUploadRequest>("application/json")
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadRequestLimit))
        .RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>
    /// The body, or null when it is no JSON, names a charset that is empty or one the host cannot decode (unknown, or
    /// refused like UTF-7), cannot be decoded in the charset it names, or names a kind that is no upload's. The charset is
    /// read the way RFC 9110 allows, quoted or not, and the body is decoded in that same charset here, so the check and the
    /// read cannot disagree. A body without a kind reads as 0, which is no kind either; the converter also reads numbers,
    /// so an undefined one is left to the caller's check.
    /// </summary>
    private static async Task<EditorialUploadRequest?> ReadAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        if (!http.HasJsonContentType() || !TryGetEncoding(http, out var encoding))
        {
            return null;
        }

        var options = http.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        try
        {
            using var reader = new StreamReader(http.Body, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            return JsonSerializer.Deserialize<EditorialUploadRequest>(text, options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The encoding the content type names, UTF-8 when it names none. False for a charset that is empty (<c>""</c>), one
    /// the host does not know, or one it refuses to decode (UTF-7), which reading the body with the framework would answer
    /// with a 500. The decoder throws on invalid bytes rather than replacing them.
    /// </summary>
    private static bool TryGetEncoding(HttpRequest http, out Encoding encoding)
    {
        encoding = StrictUtf8;
        var charset = MediaTypeHeaderValue.Parse(http.ContentType).Charset;
        if (!charset.HasValue)
        {
            return true;
        }

        var name = HeaderUtilities.RemoveQuotes(charset).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        try
        {
            encoding = Encoding.GetEncoding(name.ToString(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
