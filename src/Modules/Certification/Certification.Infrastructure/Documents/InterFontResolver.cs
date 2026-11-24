using PdfSharp.Fonts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;

/// <summary>
/// Serves Inter Regular and Bold from the assembly's embedded resources to PDFsharp (#50). PDFsharp's Core build finds
/// no system fonts on Linux, and the PDF embeds what it is given, so every face the certificate uses comes from here.
/// Any family resolves to Inter: the certificate names no other.
/// </summary>
internal sealed class InterFontResolver : IFontResolver
{
    public const string Family = "Inter";

    private const string Regular = "Inter-Regular";

    private const string Bold = "Inter-Bold";

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? Bold : Regular);

    public byte[]? GetFont(string faceName)
    {
        var resource = $"Certification.Fonts.{faceName}.ttf";
        using var stream = typeof(InterFontResolver).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The font {faceName} is not embedded in {typeof(InterFontResolver).Assembly.GetName().Name} as {resource}.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
