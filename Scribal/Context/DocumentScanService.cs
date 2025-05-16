using System.IO.Abstractions;

namespace Scribal.Context;

public class DocumentInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string RelativePath { get; set; } = string.Empty;
    public List<HeaderInfo> Headers { get; set; } = new();

    public override string ToString()
    {
        return $"{RelativePath} - {Headers.Count} headers";
    }
}

public class DirectoryNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<DirectoryNode> Subdirectories { get; set; } = new();
    public List<DocumentInfo> Documents { get; set; } = new();

    public DirectoryNode(string path, IFileSystem fileSystem)
    {
        Path = path;
        Name = fileSystem.Path.GetFileName(path);

        // If it's the root directory and the name is empty, use the directory name
        if (string.IsNullOrEmpty(Name))
        {
            Name = fileSystem.DirectoryInfo.New(path).Name;
        }
    }

    public override string ToString()
    {
        return $"{Name} ({Documents.Count} documents, {Subdirectories.Count} subdirectories)";
    }
}

public interface IDocumentScanService
{
    Task<DirectoryNode> ScanDirectoryForMarkdownAsync(IDirectoryInfo rootDirectory);
}

public class DocumentScanService(IFileSystem fileSystem) : IDocumentScanService
{
    private readonly string[] _markdownExtensions =
    {
        ".md", ".markdown"
    };

    public async Task<DirectoryNode> ScanDirectoryForMarkdownAsync(IDirectoryInfo rootDirectory)
    {
        if (!rootDirectory.Exists)
        {
            return new(rootDirectory.FullName, fileSystem);
        }

        return await BuildDirectoryTreeAsync(rootDirectory, rootDirectory);
    }

    private async Task<DirectoryNode> BuildDirectoryTreeAsync(IDirectoryInfo currentDirectory,
        IDirectoryInfo rootDirectory)
    {
        var directoryNode = new DirectoryNode(currentDirectory.FullName, fileSystem);

        // Process all markdown files in the current directory
        var markdownFiles = currentDirectory.GetFiles()
                                            .Where(file =>
                                            {
                                                // Ignore hidden files.
                                                if (file.Name.StartsWith('.'))
                                                {
                                                    return false;
                                                }

                                                var extension = fileSystem.Path.GetExtension(file.FullName)
                                                                          .ToLowerInvariant();

                                                return _markdownExtensions.Contains(extension);
                                            })
                                            .ToList();

        foreach (var file in markdownFiles)
        {
            var documentInfo = await ProcessMarkdownFileAsync(file, rootDirectory);
            directoryNode.Documents.Add(documentInfo);
        }

        // Process all non-hidden subdirectories
        var subdirectories = currentDirectory.GetDirectories().Where(d => !d.Name.StartsWith('.'));

        foreach (var subdirectory in subdirectories)
        {
            var subdirectoryNode = await BuildDirectoryTreeAsync(subdirectory, rootDirectory);
            directoryNode.Subdirectories.Add(subdirectoryNode);
        }

        return directoryNode;
    }

    private async Task<DocumentInfo> ProcessMarkdownFileAsync(IFileInfo file, IDirectoryInfo rootDirectory)
    {
        var content = await fileSystem.File.ReadAllTextAsync(file.FullName);
        var headers = MarkdownMapExtractor.ExtractHeaders(content);

        var relativePath = fileSystem.Path.GetRelativePath(rootDirectory.FullName, file.FullName);

        return new()
        {
            FilePath = file.FullName,
            RelativePath = relativePath,
            Headers = headers
        };
    }
}