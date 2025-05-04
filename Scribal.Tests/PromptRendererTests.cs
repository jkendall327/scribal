using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection;
using Microsoft.SemanticKernel;
using Moq;
using Scribal.AI;

namespace Scribal.Tests;

public class PromptRendererTests
{
    [Fact]
    public async Task RenderPromptTemplateFromFileAsync_ShouldRenderTemplate()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var assemblyLocation = "/fake/path/Scribal.dll";
        var promptsDirectory = "/fake/path/Prompts";
        var templatePath = "/fake/path/Prompts/TestTemplate.hbs";
        var templateContent = "Hello {{name}}!";
        
        // Setup mock file system
        mockFileSystem.AddFile(templatePath, new MockFileData(templateContent));
        mockFileSystem.AddDirectory(promptsDirectory);
        
        // Create a mock Assembly to return our fake path
        var mockAssembly = new Mock<Assembly>();
        mockAssembly.Setup(a => a.Location).Returns(assemblyLocation);
        
        // Create the renderer with our mock file system
        var renderer = new PromptRenderer(mockFileSystem);
        
        // Use reflection to set the private field for the assembly
        var fieldInfo = typeof(PromptRenderer).GetField("_assembly", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        fieldInfo?.SetValue(renderer, mockAssembly.Object);
        
        // Create kernel and arguments
        var kernel = Kernel.Builder.Build();
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
        var result = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);
        
        // Assert
        Assert.Equal("Hello World!", result);
    }
}
