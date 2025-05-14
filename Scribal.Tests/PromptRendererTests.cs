using System.IO.Abstractions.TestingHelpers;
using Microsoft.SemanticKernel;
using Scribal.Context;

namespace Scribal.Tests;

public class PromptRendererTests
{
    [Fact]
    public async Task RenderPromptTemplateFromFileAsync_ShouldRenderTemplate()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var templatePath = "/fake/Prompts/TestTemplate.hbs";
        var templateContent = "Hello {{name}}!";

        // Setup mock file system
        mockFileSystem.AddFile(templatePath, new(templateContent));

        // Create the renderer with our mock file system
        var renderer = new PromptRenderer(mockFileSystem);

        // Create kernel and arguments
        var kernel = Kernel.CreateBuilder().Build();
        var arguments = new KernelArguments
        {
            ["name"] = "World"
        };

        var request = new RenderRequest(
            "TestTemplate",
            "Test",
            "A test template",
            arguments);

        // Act
        var result = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, "/fake/Prompts/");

        // Assert
        Assert.Equal("Hello World!", result);
    }
}
