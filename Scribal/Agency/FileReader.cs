using System.ComponentModel;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Scribal.Agency;

public class FileReader(IFileSystem fileSystem, ILogger<FileReader> logger)
{
    public const string FileNotFoundError = "[ERROR: the file did not exist.]";
    public const string AccessDeniedError = "[ERROR: Access denied. File is outside the current working directory.]";

    [KernelFunction]
    [Description("Fetches the full content of the file specified by the filepath.")]
    public async Task<string> ReadFileContentAsync(string filepath)
    {
        var fileFullPath = fileSystem.Path.GetFullPath(filepath);

        if (!fileSystem.File.Exists(fileFullPath))
        {
            logger.LogWarning("File not found at path {FilePath}", fileFullPath);

            return FileNotFoundError;
        }

        // Get absolute paths
        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var cwdFull = fileSystem.Path.GetFullPath(cwd).TrimEnd(fileSystem.Path.DirectorySeparatorChar) +
                      fileSystem.Path.DirectorySeparatorChar;

        // Check if the file is inside the CWD
        if (!fileFullPath.StartsWith(cwdFull, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Access denied for file {FilePath} as it is outside the current working directory {CwdPath}",
                fileFullPath,
                cwdFull);

            return AccessDeniedError;
        }

        logger.LogInformation("Reading file content from {FilePath}", fileFullPath);

        return await fileSystem.File.ReadAllTextAsync(fileFullPath);
    }
}