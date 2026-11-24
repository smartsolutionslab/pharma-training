using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// The markers of the stored events Learning announced (#277), in memory. Each one added is recorded in
/// <see cref="Steps"/>, which the unit of work shares, so a test sees that the marker is added before the commit that
/// carries its announcement: committing the module's unit of work stores both together.
/// </summary>
internal sealed class InMemoryAnnouncedEvents(List<string> steps) : IAnnouncedEvents
{
    public const string Marked = "mark";

    public List<AnnouncedEvent> Added { get; } = [];

    public void Add(AnnouncedEvent announced)
    {
        Added.Add(announced);
        steps.Add(Marked);
    }
}
