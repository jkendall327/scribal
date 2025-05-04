using System.IO.Abstractions;
using System.Reflection;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

namespace Scribal.AI;

public class CommitGenerator(IFileSystem fileSystem)
{
    private readonly HandlebarsPromptTemplateFactory _templateFactory = new();
    
    public async Task<string> GetCommitMessage(Kernel kernel,
        List<string> diffs,
        string? serviceId = null,
        CancellationToken ct = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(serviceId + "-weak");

        var prompt = await RenderCommitPromptTemplateAsync(kernel, diffs);
        
        var response = await chat.GetChatMessageContentAsync(prompt, kernel: kernel, cancellationToken: ct);
        
        return response.Content ?? throw new InvalidOperationException("Assistant failed to return a commit message.");
    }
    
    private async Task<string> RenderCommitPromptTemplateAsync(Kernel kernel, List<string> diffs)
    {
        // Define the Handlebars template
        var contentRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var path = Path.Combine(contentRoot, "Prompts", "Commits.md");
        var template = await fileSystem.File.ReadAllTextAsync(path);

        // Create prompt template configuration
        var promptConfig = new PromptTemplateConfig
        {
            Template = template,
            TemplateFormat = "handlebars",
            Name = "GitCommitSummaryTemplate",
            Description = "Template for generating Git commit messages from diffs"
        };

        // Create the prompt template
        var promptTemplate = _templateFactory.Create(promptConfig);

        // Render the template with the provided diffs
        return await promptTemplate.RenderAsync(
            kernel,
            new()
            {
                ["diffs"] = diffs
            }
        );
    }
}