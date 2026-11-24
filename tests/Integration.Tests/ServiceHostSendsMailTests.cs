using Aspire.Hosting;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The AppHost reads whether a service host sends mail from its own project, as it reads its module's name:
/// <c>&lt;SendsMail&gt;</c> beside <c>&lt;ModuleName&gt;</c>, absent meaning false, true or false in any letter case,
/// and any other value stops the AppHost naming the project and the value (docs/adr/0004). The projects here are
/// temporary files; nothing is built or started.
/// </summary>
public sealed class ServiceHostSendsMailTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pharma-training-sends-mail", Guid.CreateVersion7().ToString("N"));

    public ServiceHostSendsMailTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void A_host_without_SendsMail_sends_none()
    {
        var project = Project(sendsMail: null);

        ServiceHostProjects.SendsMail(project).ShouldBeFalse();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void SendsMail_true_in_any_case_sends_mail(string value, bool sends)
    {
        var project = Project(value);

        ServiceHostProjects.SendsMail(project).ShouldBe(sends, $"<SendsMail>{value}</SendsMail>");
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void SendsMail_with_another_value_stops_the_apphost_naming_the_project(string value)
    {
        var project = Project(value);

        var stopped = Should.Throw<InvalidOperationException>(() => ServiceHostProjects.SendsMail(project), $"<SendsMail>{value}</SendsMail> was read");

        stopped.Message.ShouldContain(project, Case.Sensitive);
        stopped.Message.ShouldContain($"\"{value}\"", Case.Sensitive);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A service host's project file as <c>fabric add-module</c> writes it, with or without <c>&lt;SendsMail&gt;</c>.</summary>
    private string Project(string? sendsMail)
    {
        var path = Path.Combine(directory, Guid.CreateVersion7().ToString("N"), "Host", "Host.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var flag = sendsMail is null ? string.Empty : $"\n    <SendsMail>{sendsMail}</SendsMail>";
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <RootNamespace>SmartSolutionsLab.PharmaTraining.Services.Sample.Host</RootNamespace>
                <ModuleName>sample</ModuleName>{flag}
              </PropertyGroup>

            </Project>
            """);
        return path;
    }
}
