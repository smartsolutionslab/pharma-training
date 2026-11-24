using Microsoft.Extensions.DependencyModel;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging.Tests;

/// <summary>
/// <c>ModuleUnitOfWork.TryCommitAsync</c> runs the steps of WolverineFx.EntityFrameworkCore 6.64.0's
/// <c>DbContextOutbox&lt;T&gt;.SaveChangesAndFlushMessagesAsync</c> itself (save, commit, then flush), so that only a failure
/// before the commit is a lost race. A Wolverine update that adds a step there would be skipped silently on that path,
/// and Dependabot groups minor updates. So the version is pinned here: when this fails, read the new version's
/// <c>SaveChangesAndFlushMessagesAsync</c> and <c>EfCoreOutboxFlushing</c>, bring <c>TryCommitAsync</c> in line, and
/// only then move this expectation to the new version.
/// </summary>
public class WolverineVersionTests
{
    private static readonly Version Checked = new(6, 64, 0);

    [Fact]
    public void The_wolverine_ef_core_outbox_is_the_version_try_commit_async_was_checked_against()
    {
        // Wolverine's assemblies carry no version of their own (0.0.0.0), so the package NuGet resolved is read from the
        // test's dependency manifest (deps.json), the same one the module hosts are built with.
        var package = DependencyContext.Default.ShouldNotBeNull().RuntimeLibraries
            .Single(library => library.Name == "WolverineFx.EntityFrameworkCore");

        Version.Parse(package.Version).ShouldBe(
            Checked,
            "WolverineFx.EntityFrameworkCore moved: recheck ModuleUnitOfWork.TryCommitAsync against its SaveChangesAndFlushMessagesAsync before moving this version.");
    }
}
