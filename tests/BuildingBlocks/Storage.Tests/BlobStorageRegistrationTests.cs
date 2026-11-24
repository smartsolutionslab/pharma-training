using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

public class BlobStorageRegistrationTests
{
    [Fact]
    public void Registering_twice_registers_IBlobUrls_once_and_it_resolves_without_a_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());

        services.AddModuleBlobStorage(configuration);
        services.AddModuleBlobStorage(configuration);

        services.Count(service => service.ServiceType == typeof(IBlobUrls)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBlobUrls>().ShouldBeOfType<AzureBlobUrls>();
    }

    [Fact]
    public void Registering_twice_registers_IStoredBlobs_once_and_it_resolves_without_a_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());

        services.AddModuleBlobStorage(configuration);
        services.AddModuleBlobStorage(configuration);

        services.Count(service => service.ServiceType == typeof(IStoredBlobs)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStoredBlobs>().ShouldBeOfType<AzureStoredBlobs>();
    }

    [Fact]
    public void Blob_copies_are_registered_once_per_host()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());

        services.AddModuleBlobStorage(configuration);
        services.AddModuleBlobStorage(configuration);

        services.Count(service => service.ServiceType == typeof(IBlobCopies)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBlobCopies>().ShouldBeOfType<AzureBlobCopies>();
    }

    [Fact]
    public void Blob_writes_are_registered_once_per_host_and_resolve_without_a_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());

        services.AddModuleBlobStorage(configuration);
        services.AddModuleBlobStorage(configuration);

        services.Count(service => service.ServiceType == typeof(IBlobWrites)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBlobWrites>().ShouldBeOfType<AzureBlobWrites>();
    }
}
