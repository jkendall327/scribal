using System.ComponentModel;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Scribal.Agency;

public class FileReader(IFileSystem fileSystem, FileAccessChecker checker, ILogger<FileReader> logger)
{
    public const string FileNotFoundError = "[ERROR: the file did not exist.]";
    public const string AccessDeniedError = "[ERROR: Access denied. File is outside the current working directory.]";

    [KernelFunction]
    [Description("Fetches the full content of the file specified by the filepath.")]
    public async Task<string> ReadFileContentAsync(string filepath)
    {
        var full = fileSystem.Path.GetFullPath(filepath);

        if (!fileSystem.File.Exists(full))
        {
            logger.LogWarning("File not found at path {FilePath}", full);

            return FileNotFoundError;
        }

        var ok = checker.IsInCurrentWorkingDirectory(filepath);

        if (!ok)
        {
            logger.LogWarning("Access denied for file {FilePath} as it is outside the current working directory",
                full);

            return AccessDeniedError;
        }

        logger.LogInformation("Reading file content from {FilePath}", full);

        return await fileSystem.File.ReadAllTextAsync(full);
    }
}