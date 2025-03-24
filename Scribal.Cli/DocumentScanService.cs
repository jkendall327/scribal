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

public interface IDocumentScanService
{
    Task<List<DocumentInfo>> ScanDirectoryForMarkdownAsync(string rootDirectory);
}

public class DocumentScanService : IDocumentScanService
{
    private readonly string[] _markdownExtensions = { ".md", ".markdown" };

    public async Task<List<DocumentInfo>> ScanDirectoryForMarkdownAsync(string rootDirectory)
    {
        var result = new List<DocumentInfo>();
        
        if (!Directory.Exists(rootDirectory))
        {
            return result;
        }

        var markdownFiles = Directory.GetFiles(rootDirectory, "*.*", SearchOption.AllDirectories)
            .Where(file => _markdownExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();

        foreach (var filePath in markdownFiles)
        {
            var documentInfo = await ProcessMarkdownFileAsync(filePath, rootDirectory);
            result.Add(documentInfo);
        }

        return result;
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
