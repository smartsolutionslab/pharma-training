using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure;

/// <summary>The markers of the stored events Learning announced (#277), in their own table; the module's unit of work saves what is added with the announcement.</summary>
public sealed class AnnouncedEvents(LearningDbContext context) : IAnnouncedEvents
{
    public void Add(AnnouncedEvent announced)
    {
        Ensure.That(announced).IsNotNull();

        context.Add(announced);
    }
}
