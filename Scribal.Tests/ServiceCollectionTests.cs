using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Scribal.Tests;

public class ServiceCollectionTests
{
    [Fact]
    public void ServiceProviderBuildsCorrectly()
    {
        var services = new ServiceCollection();

        var filesystem = Substitute.For<IFileSystem>();
        
        var config = new ConfigurationBuilder().AddInMemoryCollection([
            new("OpenAI:ApiKey", "Foo"),
        ]).Build();
        
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddScribal(filesystem, new FakeTimeProvider());
        services.AddScribalAi(config);
        
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}