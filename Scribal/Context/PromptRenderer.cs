using System.IO.Abstractions;
using System.Reflection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

namespace Scribal.AI;

public record RenderRequest(string Filename, string LogicalName, string Description, KernelArguments Arguments);

public class PromptRenderer(IFileSystem fileSystem)
{
    private readonly HandlebarsPromptTemplateFactory _templateFactory = new();

    public async Task<string> RenderPromptTemplateFromFileAsync(Kernel kernel, RenderRequest request)
    {
        (var promptFilename, var logicalName, var description, var arguments) = request;
        
        var template = await GetRawTemplate(promptFilename);

        var promptConfig = new PromptTemplateConfig
        {
            Template = template,
            TemplateFormat = "handlebars",
            Name = logicalName,
            Description = description
        };

        var promptTemplate = _templateFactory.Create(promptConfig);

        return await promptTemplate.RenderAsync(kernel, arguments);
    }

    private async Task<string> GetRawTemplate(string promptFilename)
    {
        var location = Assembly.GetExecutingAssembly().Location;
        
        var contentRoot = fileSystem.Path.GetDirectoryName(location);

        if (string.IsNullOrEmpty(contentRoot))
        {
            throw new InvalidOperationException("Somehow, Assembly.GetExecutingAssembly failed to return a valid path");
        }
        
        var path = fileSystem.Path.Combine(contentRoot, "Prompts", $"{promptFilename}.hbs");
        
        var template = await fileSystem.File.ReadAllTextAsync(path);
        
        return template;
    }
}