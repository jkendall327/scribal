using System.IO.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

namespace Scribal.Context;

public record RenderRequest(string Filename, string LogicalName, string Description, KernelArguments Arguments);

public class PromptRenderer(IFileSystem fileSystem)
{
    private readonly HandlebarsPromptTemplateFactory _templateFactory = new();

    public async Task<string> RenderPromptTemplateFromFileAsync(Kernel kernel,
        RenderRequest request,
        string? promptsFolder = null,
        CancellationToken cancellationToken = default)
    {
        (var promptFilename, var logicalName, var description, var arguments) = request;

        promptsFolder ??= GetPromptsFolder();

        var path = fileSystem.Path.Combine(promptsFolder, $"{promptFilename}.hbs");

        var template = await fileSystem.File.ReadAllTextAsync(path, cancellationToken);

        var promptConfig = new PromptTemplateConfig
        {
            Template = template,
            TemplateFormat = "handlebars",
            Name = logicalName,
            Description = description
        };

        var promptTemplate = _templateFactory.Create(promptConfig);

        return await promptTemplate.RenderAsync(kernel, arguments, cancellationToken);
    }

    private string GetPromptsFolder()
    {
        var location = AppContext.BaseDirectory;

        var contentRoot = fileSystem.Path.GetDirectoryName(location);

        if (string.IsNullOrEmpty(contentRoot))
        {
            throw new InvalidOperationException("Somehow, AppContext.BaseDirectory failed to return a valid path");
        }

        var path = fileSystem.Path.Combine(contentRoot, "Prompts");

        return path;
    }
}