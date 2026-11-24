using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// Each module's announcement sweep (#277) registers its settings under its own key, its command, so two sweeps in one
/// container never read each other's: a plain singleton would leave the last module's settings to every handler.
/// </summary>
public class AnnouncementSweepsTests
{
    [Fact]
    public void Each_sweep_resolves_the_settings_of_its_own_module()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Learning:AnnouncementSweep:BatchSize", "7"),
                new KeyValuePair<string, string?>("Assessment:AnnouncementSweep:BatchSize", "11"),
            ])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnnouncementSweep<LearningSweep>(configuration, "Learning");
        services.AddAnnouncementSweep<AssessmentSweep>(configuration, "Assessment");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<AnnouncementSweepSettings>(typeof(LearningSweep)).BatchSize.ShouldBe(7);
        provider.GetRequiredKeyedService<AnnouncementSweepSettings>(typeof(AssessmentSweep)).BatchSize.ShouldBe(11);
    }

    /// <summary>A sweep's command as one module would declare it.</summary>
    public sealed record LearningSweep : ICommand<Result<Unit, ApiError>>;

    /// <summary>A sweep's command as another module would declare it.</summary>
    public sealed record AssessmentSweep : ICommand<Result<Unit, ApiError>>;
}
