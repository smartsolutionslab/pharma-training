namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>The absolute URL of a page of the web app.</summary>
public interface IFrontendLinks
{
    /// <param name="pathAndFragment">A path relative to the web app's root, starting with <c>/</c>, with its query or fragment.</param>
    Uri LinkTo(string pathAndFragment);
}
