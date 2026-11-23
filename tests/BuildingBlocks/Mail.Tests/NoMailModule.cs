using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// A module that sends no mail, whose infrastructure a host registers without the mail settings (docs/adr/0004). It
/// lives in this test assembly, so it has no Domain, Application or Infrastructure project: while
/// <see cref="EmptyLayers"/> resolves assemblies, each of its layers is an empty assembly of the name
/// <see cref="ModuleAssemblies"/> asks for.
/// </summary>
public sealed class NoMailModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleInfrastructure<NoMailModule>(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    /// <summary>Resolves a layer of this module (named after this assembly) to an empty assembly of that name.</summary>
    public static Assembly? EmptyLayers(AssemblyLoadContext context, AssemblyName name)
    {
        var module = typeof(NoMailModule).Assembly.GetName().Name!;
        var prefix = module[..module.LastIndexOf('.')] + ".";
        if (name.Name is { } requested && requested.StartsWith(prefix, StringComparison.Ordinal))
        {
            var layer = new PersistedAssemblyBuilder(new AssemblyName(requested), typeof(object).Assembly);
            layer.DefineDynamicModule(requested);
            using var image = new MemoryStream();
            layer.Save(image);
            image.Position = 0;
            return context.LoadFromStream(image);
        }

        return null;
    }
}
