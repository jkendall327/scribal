﻿using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Scribal.Cli;

namespace Scribal.Tests;

public class ServiceCollectionTests
{
    [Fact]
    public void ServiceProviderBuildsCorrectly()
    {
        var services = new ServiceCollection();

        var filesystem = Substitute.For<IFileSystem>();
        
        var config = new ConfigurationBuilder().AddInMemoryCollection([
            new("AIServices:OpenAI:ApiKey", "Foo"),
            new("AIServices:OpenAI:ModelId", "Foo"),
        ]).Build();
        
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddScribal(config, filesystem, new FakeTimeProvider());
        services.AddScribalAi(config);
        services.AddScribalInterface();
        
        _ = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}