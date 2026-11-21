using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>Links into the web app at <c>Frontend:BaseUrl</c>. Stub for #9's red tests (T6).</summary>
public sealed class FrontendLinks : IFrontendLinks
{
    public FrontendLinks(IConfiguration configuration) => Ensure.That(configuration).IsNotNull();

    public Uri LinkTo(string pathAndFragment) => throw new NotImplementedException();
}
