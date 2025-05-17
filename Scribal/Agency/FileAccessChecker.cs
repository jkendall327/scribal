using System.IO.Abstractions;

namespace Scribal.Agency;

public class FileAccessChecker(IFileSystem fileSystem)
{
    public const string FileNotFoundError = "[ERROR: the file did not exist.]";
    public const string AccessDeniedError = "[ERROR: Access denied. File is outside the current working directory.]";
    
    public bool IsInCurrentWorkingDirectory(string filepath)
    {
        var fileFullPath = fileSystem.Path.GetFullPath(filepath);

        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var cwdFull = fileSystem.Path.GetFullPath(cwd).TrimEnd(fileSystem.Path.DirectorySeparatorChar) +
                      fileSystem.Path.DirectorySeparatorChar;

        return fileFullPath.StartsWith(cwdFull, StringComparison.OrdinalIgnoreCase);
    }
}