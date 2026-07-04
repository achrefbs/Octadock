using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.DependencyInjection;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.DependencyInjection;

public class CoreServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(string? dataRoot = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICaptureRepository>());
        services.AddSingleton(Substitute.For<ISettingsStore>());
        services.AddOctadockCore(dataRoot);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(IClock))]
    [InlineData(typeof(IStoragePaths))]
    [InlineData(typeof(ICommandParser))]
    [InlineData(typeof(ICommandFormatter))]
    [InlineData(typeof(IFilenameGenerator))]
    [InlineData(typeof(ISettingsService))]
    [InlineData(typeof(IProjectSerializer))]
    [InlineData(typeof(IRetentionService))]
    public void All_core_services_resolve(Type serviceType)
    {
        using ServiceProvider provider = Build();
        provider.GetService(serviceType).Should().NotBeNull();
    }

    [Fact]
    public void Registers_expected_concrete_types()
    {
        using ServiceProvider provider = Build();
        provider.GetRequiredService<IClock>().Should().BeOfType<SystemClock>();
        provider.GetRequiredService<IStoragePaths>().Should().BeOfType<StoragePaths>();
        provider.GetRequiredService<ICommandParser>().Should().BeOfType<CommandParser>();
        provider.GetRequiredService<ICommandFormatter>().Should().BeOfType<CommandFormatter>();
        provider.GetRequiredService<IFilenameGenerator>().Should().BeOfType<FilenameGenerator>();
        provider.GetRequiredService<ISettingsService>().Should().BeOfType<SettingsService>();
        provider.GetRequiredService<IProjectSerializer>().Should().BeOfType<OctadockProjectSerializer>();
        provider.GetRequiredService<IRetentionService>().Should().BeOfType<RetentionService>();
    }

    [Fact]
    public void Storage_paths_singleton_uses_supplied_data_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "OctadockDi", Guid.NewGuid().ToString("N"));
        using ServiceProvider provider = Build(root);
        provider.GetRequiredService<IStoragePaths>().RootDirectory.Should().Be(Path.GetFullPath(root));
    }

    [Fact]
    public void Stateless_services_are_singletons()
    {
        using ServiceProvider provider = Build();
        provider.GetRequiredService<ICommandParser>()
            .Should().BeSameAs(provider.GetRequiredService<ICommandParser>());
        provider.GetRequiredService<ISettingsService>()
            .Should().BeSameAs(provider.GetRequiredService<ISettingsService>());
    }
}
