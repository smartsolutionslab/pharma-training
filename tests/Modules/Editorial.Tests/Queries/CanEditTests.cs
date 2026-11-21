using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.CanEdit;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.Queries;

public class CanEditTests
{
    [Fact]
    public async Task The_answer_names_the_edit_permission_the_endpoints_policy_granted()
    {
        var handler = new CanEditHandler();

        var result = await handler.HandleAsync(new CanEdit(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe(EditorialPolicyNames.Edit);
    }
}
