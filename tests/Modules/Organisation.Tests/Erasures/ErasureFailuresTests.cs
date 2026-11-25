using System.Net;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

public class ErasureFailuresTests
{
    [Fact]
    public void Each_erasure_failure_has_its_code_and_status()
    {
        var lastManager = ErasureFailures.LastManager();
        lastManager.Code.ShouldBe("organisation.erasure-refused-last-manager");
        lastManager.Status.ShouldBe(HttpStatusCode.Conflict);
        lastManager.Message.ShouldBe("The person is the last active manager of a pharmacy. Make another member manager there first.");

        var notFound = ErasureFailures.NotFound(Guid.CreateVersion7());
        notFound.Code.ShouldBe("organisation.erasure-not-found");
        notFound.Status.ShouldBe(HttpStatusCode.NotFound);
        notFound.Message.ShouldBe("This erasure does not exist.");
    }

    [Fact]
    public void A_lost_race_of_either_kind_is_a_concurrent_save_that_names_no_pharmacy()
    {
        LostRace[] races = [new VersionMovedOn(), new SaveDeadlocked("40P01")];

        foreach (var race in races)
        {
            var error = ErasureFailures.LostRace(race);

            error.Code.ShouldBe("organisation.concurrent-save");
            error.Status.ShouldBe(HttpStatusCode.Conflict);
        }
    }
}
