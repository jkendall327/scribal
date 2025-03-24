using System.IO;

namespace Scribal.Cli;

public class DocumentInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string RelativePath { get; set; } = string.Empty;
    public List<HeaderInfo> Headers { get; set; } = new List<HeaderInfo>();

    public override string ToString()
    {
        return $"{RelativePath} - {Headers.Count} headers";
    }
}

public class DirectoryNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<DirectoryNode> Subdirectories { get; set; } = new List<DirectoryNode>();
    public List<DocumentInfo> Documents { get; set; } = new List<DocumentInfo>();

    public DirectoryNode(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        // If it's the root directory and the name is empty, use the directory name
        if (string.IsNullOrEmpty(Name))
        {
            Name = new DirectoryInfo(path).Name;
        }
    }

    public override string ToString()
    {
        return $"{Name} ({Documents.Count} documents, {Subdirectories.Count} subdirectories)";
    }
}

public interface IDocumentScanService
{
    Task<DirectoryNode> ScanDirectoryForMarkdownAsync(string rootDirectory);
}

public class DocumentScanService : IDocumentScanService
{
    private readonly string[] _markdownExtensions = { ".md", ".markdown" };

    public async Task<DirectoryNode> ScanDirectoryForMarkdownAsync(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return new DirectoryNode(rootDirectory);
        }

        return await BuildDirectoryTreeAsync(rootDirectory, rootDirectory);
    }

    private async Task<DirectoryNode> BuildDirectoryTreeAsync(string currentDirectory, string rootDirectory)
    {
        var directoryNode = new DirectoryNode(currentDirectory);
        
        // Process all markdown files in the current directory
        var markdownFiles = Directory.GetFiles(currentDirectory)
            .Where(file => _markdownExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();

        foreach (var filePath in markdownFiles)
        {
            var documentInfo = await ProcessMarkdownFileAsync(filePath, rootDirectory);
            directoryNode.Documents.Add(documentInfo);
        }

        // Process all subdirectories
        var subdirectories = Directory.GetDirectories(currentDirectory);
        foreach (var subdirectory in subdirectories)
        {
            var subdirectoryNode = await BuildDirectoryTreeAsync(subdirectory, rootDirectory);
            directoryNode.Subdirectories.Add(subdirectoryNode);
        }

        return directoryNode;
    }

    private async Task<DocumentInfo> ProcessMarkdownFileAsync(string filePath, string rootDirectory)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var headers = MarkdownMapExtractor.ExtractHeaders(content);
        
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        
        return new DocumentInfo
        {
            FilePath = filePath,
            RelativePath = relativePath,
            Headers = headers
        };
    }
}
