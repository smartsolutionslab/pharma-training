using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class AzureIdentityStaysInStorageTests
{
    [Fact]
    public void A_using_of_Azure_Identity_in_the_llm_building_block_fails_the_rule()
    {
        const string source = """
            using Azure.Identity;

            namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

            internal static class ModelCredential
            {
                public static object Create() => new DefaultAzureCredential();
            }
            """;

        var violation = AzureIdentityStaysInStorage.Violations("src/BuildingBlocks/Llm/ModelCredential.cs", source).ShouldHaveSingleItem();

        violation.ShouldContain("src/BuildingBlocks/Llm/ModelCredential.cs:1");
        violation.ShouldContain("SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm");
    }

    [Theory]
    [InlineData("global using Azure.Identity;\n\nnamespace Microsoft.Extensions.DependencyInjection;\n")]
    [InlineData("using Credential = Azure.Identity.ManagedIdentityCredential;\n\nnamespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;\n")]
    [InlineData("namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;\n\ninternal static class ModelCredential\n{\n    public static object Create() => new Azure.Identity.DefaultAzureCredential();\n}\n")]
    [InlineData("namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.StorageAdjacent;\n\ninternal static class Probe\n{\n    public static global::Azure.Identity.ManagedIdentityCredential? Credential { get; }\n}\n")]
    public void Every_spelling_of_Azure_Identity_outside_storage_fails_the_rule(string source)
    {
        AzureIdentityStaysInStorage.Violations("src/Probe.cs", source).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage")]
    [InlineData("SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Identity")]
    public void Storage_and_the_namespaces_under_it_pass_the_rule(string ns)
    {
        var source = $"using Azure.Identity;\n\nnamespace {ns};\n\ninternal static class ServiceIdentity\n{{\n    public static object Create() => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);\n}}\n";

        AzureIdentityStaysInStorage.Violations("src/BuildingBlocks/Storage/ServiceIdentity.cs", source).ShouldBeEmpty();
    }

    [Fact]
    public void A_comment_or_a_string_that_names_Azure_Identity_is_prose()
    {
        const string source = """
            namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

            // Azure.Identity is not used here.
            internal static class Note
            {
                public const string Text = "Azure.Identity";
            }
            """;

        AzureIdentityStaysInStorage.Violations("src/BuildingBlocks/Llm/Note.cs", source).ShouldBeEmpty();
    }
}
