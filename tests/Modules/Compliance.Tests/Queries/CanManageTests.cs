using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.CanManage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Queries;

public class CanManageTests
{
    [Fact]
    public async Task The_answer_names_the_permission_the_endpoints_policy_granted()
    {
        var handler = new CanManageHandler();

        var result = await handler.HandleAsync(new CanManage(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe(CompliancePolicyNames.Manage);
    }
}
