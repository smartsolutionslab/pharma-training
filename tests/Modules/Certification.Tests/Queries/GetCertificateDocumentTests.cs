using System.Net;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetCertificateDocument;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// The holder reads their certificate's PDF through a five-minute read URL on Certification's own blob, served inline as
/// <c>application/pdf</c> (#50, docs/adr/0002). The checks run in this order, the first that fails answering: a caller
/// who is a person (403), a certificate of that person, with someone else's not found like an unknown one, so nobody
/// learns which ids exist (404), and a document already stored (409).
/// </summary>
public class GetCertificateDocumentTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCertificateStore certificates = new();
    private readonly IBlobUrls urls = Substitute.For<IBlobUrls>();
    private readonly PersonId holder = PersonId.From(Guid.CreateVersion7());

    public GetCertificateDocumentTests()
    {
        urls.CreateReadUrlAsync(Arg.Any<BlobName>(), Arg.Any<BlobUrlLifetime>(), Arg.Any<BlobReadHeaders>(), Arg.Any<CancellationToken>())
            .Returns(call => UrlFor(call.Arg<BlobName>()));
    }

    [Fact]
    public async Task The_holder_gets_a_five_minute_inline_pdf_url_on_the_stored_document()
    {
        var certificate = StoredWithDocument("certificates/2027/03/anna.pdf");

        var document = await HandleAsync(certificate.Id.Value, holder.Value);

        var expected = UrlFor(BlobName.From("certificates/2027/03/anna.pdf"));
        document.IsSuccess.ShouldBeTrue(document.IsFailure ? document.Error.Code : string.Empty);
        document.Value.ShouldBe(new CertificationDocumentUrlDto(expected.Url, expected.ExpiresAt));
        await urls.Received(1).CreateReadUrlAsync(
            BlobName.From("certificates/2027/03/anna.pdf"),
            BlobUrlLifetime.From(TimeSpan.FromMinutes(5)),
            BlobReadHeaders.Inline("application/pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_certificate_is_not_found()
    {
        var certificate = StoredWithDocument($"certificates/{Guid.CreateVersion7():D}.pdf");

        var document = await HandleAsync(certificate.Id.Value, Guid.CreateVersion7());

        ShouldBeRefused(document, HttpStatusCode.NotFound, "certification.certificate-not-found");
    }

    [Fact]
    public async Task An_unknown_certificate_is_not_found()
    {
        StoredWithDocument($"certificates/{Guid.CreateVersion7():D}.pdf");

        var document = await HandleAsync(Guid.CreateVersion7(), holder.Value);

        ShouldBeRefused(document, HttpStatusCode.NotFound, "certification.certificate-not-found");
    }

    [Fact]
    public async Task The_empty_id_is_not_found_like_any_other()
    {
        var document = await HandleAsync(Guid.Empty, holder.Value);

        ShouldBeRefused(document, HttpStatusCode.NotFound, "certification.certificate-not-found");
    }

    [Fact]
    public async Task A_certificate_whose_document_is_still_pending_answers_not_ready()
    {
        var certificate = TestCertificates.Issued(holder);
        certificates.Stored.Add(certificate);

        var document = await HandleAsync(certificate.Id.Value, holder.Value);

        ShouldBeRefused(document, HttpStatusCode.Conflict, "certification.document-not-ready");
    }

    [Fact]
    public async Task A_pending_certificate_of_someone_else_is_not_found_rather_than_not_ready()
    {
        var certificate = TestCertificates.Issued(holder);
        certificates.Stored.Add(certificate);

        var document = await HandleAsync(certificate.Id.Value, Guid.CreateVersion7());

        ShouldBeRefused(document, HttpStatusCode.NotFound, "certification.certificate-not-found");
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden_before_anything_is_looked_up()
    {
        var certificate = StoredWithDocument($"certificates/{Guid.CreateVersion7():D}.pdf");

        var document = await HandleAsync(certificate.Id.Value, Guid.Empty);

        ShouldBeRefused(document, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Fact]
    public async Task No_query_is_refused()
    {
        await Should.ThrowAsync<GuardException>(() => new GetCertificateDocumentHandler(certificates, urls).HandleAsync(null!, TestContext.Current.CancellationToken));
    }

    private static BlobUrl UrlFor(BlobName name) =>
        new(new Uri($"https://blobs.example/certification/{name.Value}?sp=r&sig=fake"), Now.AddMinutes(5));

    private Task<Result<CertificationDocumentUrlDto, ApiError>> HandleAsync(Guid certificateId, Guid caller) =>
        new GetCertificateDocumentHandler(certificates, urls).HandleAsync(new GetCertificateDocument(certificateId, caller), TestContext.Current.CancellationToken);

    private Certificate StoredWithDocument(string location)
    {
        var certificate = TestCertificates.Issued(holder);
        certificate.AttachDocument(DocumentReference.From(location));
        certificates.Stored.Add(certificate);
        return certificate;
    }

    private void ShouldBeRefused(Result<CertificationDocumentUrlDto, ApiError> document, HttpStatusCode status, string code)
    {
        document.IsFailure.ShouldBeTrue();
        document.Error.Code.ShouldBe(code);
        document.Error.Status.ShouldBe(status);
        urls.ReceivedCalls().ShouldBeEmpty("nothing is signed for a refused caller");
    }
}
