using MailKit.Security;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// The SMTP server is the connection string <c>mail</c>: <c>smtp://[user[:password]@]host[:port]</c> or <c>smtps://…</c>.
/// A password never travels in clear, and never appears in an error.
/// </summary>
public class SmtpSettingsTests
{
    private const string Key = "ConnectionStrings:mail";

    [Fact]
    public void A_server_without_credentials_uses_tls_when_it_offers_it()
    {
        var settings = SmtpSettings.From(Settings.Of((Key, "smtp://localhost:1025")));

        settings.Host.ShouldBe("localhost");
        settings.Port.ShouldBe(1025);
        settings.User.ShouldBeNull();
        settings.Password.ShouldBeNull();
        settings.Security.ShouldBe(SecureSocketOptions.StartTlsWhenAvailable);
    }

    [Theory]
    [InlineData("smtp://mail.example")]
    [InlineData("smtp://mail.example:1025")]
    [InlineData("smtp://10.0.0.5:25")]
    [InlineData("smtp://192.168.1.20")]
    [InlineData("smtp://localhost.mail.example")]
    [InlineData("smtp://mailpit")]
    public void A_server_that_is_not_loopback_requires_starttls_even_without_credentials(string value) =>
        SmtpSettings.From(Settings.Of((Key, value))).Security.ShouldBe(SecureSocketOptions.StartTls);

    [Theory]
    [InlineData("smtp://localhost:1025")]
    [InlineData("smtp://127.0.0.1:1025")]
    [InlineData("smtp://[::1]:1025")]
    public void A_loopback_server_without_credentials_uses_tls_only_when_it_offers_it(string value) =>
        SmtpSettings.From(Settings.Of((Key, value))).Security.ShouldBe(SecureSocketOptions.StartTlsWhenAvailable);

    [Fact]
    public void A_server_with_credentials_requires_starttls_and_its_credentials_are_url_decoded()
    {
        var settings = SmtpSettings.From(Settings.Of((Key, "smtp://user:p%40ss@mail.example:587")));

        settings.Host.ShouldBe("mail.example");
        settings.Port.ShouldBe(587);
        settings.User.ShouldBe("user");
        settings.Password.ShouldBe("p@ss");
        settings.Security.ShouldBe(SecureSocketOptions.StartTls);
    }

    [Fact]
    public void Smtps_connects_with_tls_on_port_465_by_default()
    {
        var settings = SmtpSettings.From(Settings.Of((Key, "smtps://mail.example")));

        settings.Host.ShouldBe("mail.example");
        settings.Port.ShouldBe(465);
        settings.Security.ShouldBe(SecureSocketOptions.SslOnConnect);
    }

    [Fact]
    public void Smtp_without_a_port_uses_port_25() =>
        SmtpSettings.From(Settings.Of((Key, "smtp://mail.example"))).Port.ShouldBe(25);

    [Theory]
    [InlineData("http://user:s3cret@mail.example")]
    [InlineData("smtp://")]
    [InlineData("user:s3cret@mail.example:25")]
    [InlineData("")]
    [InlineData(null)]
    public void A_missing_or_unusable_connection_string_is_named_without_its_password(string? value)
    {
        var refused = Should.Throw<InvalidOperationException>(() => SmtpSettings.From(Settings.Of((Key, value))));

        refused.Message.ShouldContain(Key);
        refused.Message.ShouldNotContain("s3cret");
    }
}
