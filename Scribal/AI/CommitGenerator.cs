using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Scribal.AI;

public class CommitGenerator(PromptRenderer renderer)
{
    public async Task<string> GetCommitMessage(Kernel kernel,
        List<string> diffs,
        string? serviceId = null,
        CancellationToken ct = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(serviceId + "-weak");

        var request = new RenderRequest("Commits",
            "GitCommitSummaryTemplate",
            "Template for generating Git commit messages from diffs",
            new()
            {
                ["diffs"] = diffs
            });
        
        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);

        var response = await chat.GetChatMessageContentAsync(prompt, kernel: kernel, cancellationToken: ct);

        return response.Content ?? throw new InvalidOperationException("Assistant failed to return a commit message.");
    }
}