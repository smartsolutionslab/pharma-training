namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class BlobStorageSdkStaysInBuildingBlocks : IArchitectureRule
{
    private const string AzureStorage = "Azure.Storage";

    public string Description => "No module assembly (Domain, Application, Infrastructure, Api) references the Azure Storage SDK: modules take IBlobUrls from src/BuildingBlocks/Storage.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        foreach (var assembly in model.ModuleAssemblies)
        {
            foreach (var reference in assembly.GetReferencedAssemblies().Select(reference => reference.Name!).Where(IsAzureStorage).Order(StringComparer.Ordinal))
            {
                yield return $"{assembly.GetName().Name} references {reference}, but modules take IBlobUrls from src/BuildingBlocks/Storage, never the Azure Storage SDK.";
            }
        }
    }

    private static bool IsAzureStorage(string reference) =>
        reference == AzureStorage || reference.StartsWith(AzureStorage + ".", StringComparison.Ordinal);
}
