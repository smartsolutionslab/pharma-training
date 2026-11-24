using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>The database's clock, read from the <see cref="TimeProvider"/> given (a <c>FakeTimeProvider</c> in tests).</summary>
internal sealed class FakeDatabaseClock(TimeProvider time) : IDatabaseClock
{
    public Task<Timestamp> NowAsync(CancellationToken cancellationToken) => Task.FromResult(Timestamp.From(time.GetUtcNow()));
}
