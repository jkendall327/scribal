using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Scribal.Agency;

public class VectorSearch
{
    public const string VectorSearchToolName = "search";
    
    [KernelFunction(VectorSearchToolName), Description("Searches for similar content.")]
    public async Task ApplyUnifiedDiffAsync([Description("The path to the file to edit.")] string query)
    {
        throw new NotImplementedException();
    }
}