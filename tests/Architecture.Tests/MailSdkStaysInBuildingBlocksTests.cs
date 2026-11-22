using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class MailSdkStaysInBuildingBlocksTests
{
    [Fact]
    public void The_building_blocks_assembly_which_sends_mail_would_fail_the_rule()
    {
        var violations = MailSdkStaysInBuildingBlocks.Violations([typeof(IMailSender).Assembly]).ToList();

        violations.Count.ShouldBe(2, string.Join(" | ", violations));
        violations.ShouldContain(violation => violation.Contains("references MailKit,", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("references MimeKit,", StringComparison.Ordinal));
    }

    [Fact]
    public void The_modules_pass_the_rule()
    {
        MailSdkStaysInBuildingBlocks.Violations(ArchitectureModel.Load().ModuleAssemblies).ShouldBeEmpty();
    }
}
