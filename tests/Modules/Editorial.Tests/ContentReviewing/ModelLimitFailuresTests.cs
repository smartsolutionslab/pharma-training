using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>An editor over a limit on the model's endpoints (#174) is refused with one code at 429, whichever endpoint it was.</summary>
public class ModelLimitFailuresTests
{
    [Fact]
    public void The_limit_reached_is_429_editorial_model_limit_reached()
    {
        var error = ModelLimitFailures.Reached();

        error.ShouldBeOfType<ModelLimitReached>();
        error.Code.ShouldBe("editorial.model-limit-reached");
        error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        error.Message.ShouldBe("You have reached your limit of AI requests; try again later.");
    }
}
