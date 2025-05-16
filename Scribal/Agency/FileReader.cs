using System.ComponentModel;
using System.IO.Abstractions;
using Microsoft.SemanticKernel;

namespace Scribal.Agency;

public class FileReader(IFileSystem fileSystem)
{
    [KernelFunction]
    [Description("Fetches the full content of the file specified by the filepath.")]
    public async Task<string> ReadFileContentAsync(string filepath)
    {
        if (!fileSystem.File.Exists(filepath))
        {
            return "[ERROR: the file did not exist.]";
        }

        return await fileSystem.File.ReadAllTextAsync(filepath);
    }
}