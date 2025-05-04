using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Scribal.AI;

public class CommitGenerator
{
    public async Task<string> GetCommitMessage(Kernel kernel,
        List<string> diffs,
        string? serviceId = null,
        CancellationToken ct = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(serviceId + "-weak");

        var sb = new StringBuilder("Provide a concise Git commit summary for the following diff(s).");

        sb.AppendLine("---");
        
        foreach (var diff in diffs)
        {
            sb.AppendLine(diff);
        }
        
        var response = await chat.GetChatMessageContentAsync(sb.ToString(), kernel: kernel, cancellationToken: ct);
        
        return response.Content ?? throw new InvalidOperationException("Assistant failed to return a commit message.");
    }
}