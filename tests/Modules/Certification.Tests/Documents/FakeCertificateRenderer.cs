using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Documents;

/// <summary>Renders no PDF: answers fixed bytes, or throws what it was told to, and keeps the content it was given.</summary>
internal sealed class FakeCertificateRenderer : ICertificateRenderer
{
    public static readonly byte[] Rendered = "%PDF-1.7 rendered"u8.ToArray();

    public List<CertificateContent> Rendering { get; } = [];

    public Exception? Failure { get; set; }

    public byte[] Render(CertificateContent content)
    {
        Rendering.Add(content);
        if (Failure is not null)
        {
            throw Failure;
        }

        return Rendered;
    }
}
