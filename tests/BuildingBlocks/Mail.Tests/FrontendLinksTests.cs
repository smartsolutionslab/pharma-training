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
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://[::1]:5173")]
    [InlineData("https://app.example")]
    [InlineData("https://localhost:5173")]
    public void A_base_url_over_https_or_over_http_to_loopback_is_used(string baseUrl) =>
        new FrontendLinks(Settings.Of((Key, baseUrl))).LinkTo("/invitations/accept#token=abc").AbsoluteUri
            .ShouldBe($"{new Uri(baseUrl).AbsoluteUri.TrimEnd('/')}/invitations/accept#token=abc");

    [Theory]
    [InlineData("http://app.example")]
    [InlineData("http://app.example:8080/")]
    [InlineData("http://10.0.0.5:5173")]
    [InlineData("http://localhost.app.example")]
    public void A_base_url_over_http_to_a_host_that_is_not_loopback_is_refused_because_the_link_carries_a_token(string baseUrl) =>
        Should.Throw<InvalidOperationException>(() => new FrontendLinks(Settings.Of((Key, baseUrl))).LinkTo("/invitations/accept#token=abc"))
            .Message.ShouldContain(Key);

    [Fact]
    public void A_base_url_supplied_after_a_link_failed_is_used_by_the_next_link()
    {
        var configuration = Settings.Of((Key, null));
        var links = new FrontendLinks(configuration);
        Should.Throw<InvalidOperationException>(() => links.LinkTo("/invitations/accept#token=abc"));

        configuration[Key] = "https://app.example";

        Should.NotThrow(() => links.LinkTo("/invitations/accept#token=abc")).AbsoluteUri.ShouldBe("https://app.example/invitations/accept#token=abc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:5173")]
    [InlineData("/app")]
    public void A_missing_or_relative_base_url_is_named(string? baseUrl) =>
        Should.Throw<InvalidOperationException>(() => new FrontendLinks(Settings.Of((Key, baseUrl))).LinkTo("/invitations/accept#token=abc"))
            .Message.ShouldContain(Key);
}
