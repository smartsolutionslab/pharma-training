using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>Every host gets one mail sender and one link builder; neither reads its settings before it is used.</summary>
public class MailRegistrationTests
{
    [Fact]
    public void Registering_twice_registers_one_sender_and_one_link_builder_and_reads_no_configuration()
    {
        var configuration = new UnreadableConfiguration();
        var services = new ServiceCollection();

        services.AddModuleMail(configuration);
        services.AddModuleMail(configuration);

        services.Count(service => service.ServiceType == typeof(IMailSender)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(IFrontendLinks)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMailSender>().ShouldBeOfType<SmtpMailSender>();
        provider.GetRequiredService<IFrontendLinks>().ShouldBeOfType<FrontendLinks>();
    }
}
