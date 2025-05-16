using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;

#pragma warning disable SKEXP0001

namespace Scribal.Agency;

public sealed class VectorSearch(ISemanticTextMemory memory)
{
    public const string VectorSearchToolName = "search";

    [KernelFunction(VectorSearchToolName)]
    [Description("Searches for similar content. Returns the content as a string.")]
    public async Task<string> SearchAsync([Description("The path to the file to edit.")] string query,
        CancellationToken cancellationToken = default)
    {
        var name = await memory.GetCollectionsAsync(cancellationToken: cancellationToken);

        var enumerable = memory.SearchAsync(name.Single(), query, cancellationToken: cancellationToken);

        var sb = new StringBuilder();

        await foreach (var item in enumerable)
        {
            sb.AppendLine("---");
            sb.AppendLine(item.Metadata.Text);
            sb.AppendLine("---");
        }

        return sb.ToString();
    }
}