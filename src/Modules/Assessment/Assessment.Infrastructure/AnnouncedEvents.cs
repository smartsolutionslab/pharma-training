using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure;

/// <summary>The markers of the stored events Assessment announced (#277), in their own table; the module's unit of work saves what is added with the announcement.</summary>
public sealed class AnnouncedEvents(AssessmentDbContext context) : IAnnouncedEvents
{
    public void Add(AnnouncedEvent announced)
    {
        Ensure.That(announced).IsNotNull();

        context.Add(announced);
    }
}
