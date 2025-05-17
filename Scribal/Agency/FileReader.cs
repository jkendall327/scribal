using System.ComponentModel;
using System.IO.Abstractions;
using Microsoft.SemanticKernel;

namespace Scribal.Agency;

public class FileReader(IFileSystem fileSystem)
{
    public const string FileNotFoundError = "[ERROR: the file did not exist.]";
    public const string AccessDeniedError = "[ERROR: Access denied. File is outside the current working directory.]";
    
    [KernelFunction]
    [Description("Fetches the full content of the file specified by the filepath.")]
    public async Task<string> ReadFileContentAsync(string filepath)
    {
        if (!fileSystem.File.Exists(filepath))
        {
            return FileNotFoundError;
        }

        // Get absolute paths
        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var cwdFull = fileSystem.Path.GetFullPath(cwd).TrimEnd(fileSystem.Path.DirectorySeparatorChar) +
                      fileSystem.Path.DirectorySeparatorChar;

        var fileFull = fileSystem.Path.GetFullPath(filepath);

        // Check if the file is inside the CWD
        if (!fileFull.StartsWith(cwdFull, StringComparison.OrdinalIgnoreCase))
        {
            return AccessDeniedError;
        }

        return await fileSystem.File.ReadAllTextAsync(filepath);
    }
}