namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>A link into the web app is its base URL, <c>Frontend:BaseUrl</c>, and the page's path and fragment.</summary>
public class FrontendLinksTests
{
    private const string Key = "Frontend:BaseUrl";

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://localhost:5173/")]
    public void A_link_is_the_page_under_the_web_apps_base_url(string baseUrl) =>
        new FrontendLinks(Settings.Of((Key, baseUrl))).LinkTo("/invitations/accept#token=abc").AbsoluteUri
            .ShouldBe("http://localhost:5173/invitations/accept#token=abc");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:5173")]
    [InlineData("/app")]
    public void A_missing_or_relative_base_url_is_named(string? baseUrl) =>
        Should.Throw<InvalidOperationException>(() => new FrontendLinks(Settings.Of((Key, baseUrl))).LinkTo("/invitations/accept#token=abc"))
            .Message.ShouldContain(Key);
}
