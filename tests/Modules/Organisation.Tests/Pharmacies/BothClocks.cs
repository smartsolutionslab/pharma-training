using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Builds the accept handler and the mail request's consumer over the in-memory store with two clocks: the host's
/// <see cref="TimeProvider"/> and the database's <see cref="IDatabaseClock"/>. They are built through
/// <see cref="ActivatorUtilities"/>, so a test does not name either constructor and whichever clock it takes is given.
/// Without a database clock the database reads the host's.
/// </summary>
internal static class BothClocks
{
    public static AcceptInvitationHandler Accept(InMemoryPharmacyStore store, TimeProvider host, TimeProvider? database = null)
    {
        using var services = Services(store, host, database ?? host).BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<AcceptInvitationHandler>(services);
    }

    public static InvitationMailRequestedV1Consumer Consumer(
        InMemoryPharmacyStore store,
        IMailSender mails,
        IFrontendLinks links,
        TimeProvider host,
        ILogger<InvitationMailRequestedV1Consumer>? logger = null,
        TimeProvider? database = null)
    {
        using var services = Services(store, host, database ?? host)
            .AddSingleton(mails)
            .AddSingleton(links)
            .AddSingleton(logger ?? NullLogger<InvitationMailRequestedV1Consumer>.Instance)
            .BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<InvitationMailRequestedV1Consumer>(services);
    }

    private static ServiceCollection Services(InMemoryPharmacyStore store, TimeProvider host, TimeProvider database)
    {
        var services = new ServiceCollection();
        services
            .AddSingleton<IPharmacyStore>(store)
            .AddSingleton<IOrganisationUnitOfWork>(store)
            .AddSingleton(host)
            .AddSingleton<IDatabaseClock>(new FakeDatabaseClock(database));
        return services;
    }
}
