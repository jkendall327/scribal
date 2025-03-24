using System.IO.Abstractions;
using System.Text;

namespace Scribal.Cli;

public class PromptBuilder
{
    private readonly IDocumentScanService _scanService;
    private readonly IFileSystem _fileSystem;
    private readonly Dictionary<string, string> _specialFiles = new()
    {
        { "PLOT.md", "# Plot Overview" },
        { "CHARACTERS.md", "# Characters" },
        { "STYLE.md", "# Style Guide" }
    };

    public PromptBuilder(IDocumentScanService scanService, IFileSystem fileSystem)
    {
        _scanService = scanService;
        _fileSystem = fileSystem;
    }

    public async Task<string> BuildPromptAsync(IDirectoryInfo directory)
    {
        var task = _scanService.ScanDirectoryForMarkdownAsync(directory);
        
        var sb = new StringBuilder();
        
        sb.AppendLine("# Project Structure");
        sb.AppendLine("The following is a map of markdown documents in this project:");
        sb.AppendLine();

        var directoryTree = await task;
        
        AppendDirectoryStructure(sb, directoryTree, 0);
        
        // Add README content if available
        var readmeDoc = FindReadmeDocument(directoryTree);
        if (readmeDoc != null)
        {
            sb.AppendLine();
            sb.AppendLine("# Project README");
            sb.AppendLine();
            
            string readmeContent = await _fileSystem.File.ReadAllTextAsync(readmeDoc.FilePath);
            sb.AppendLine(readmeContent);
        }
        
        // Add special files if they exist
        await AppendSpecialFilesAsync(sb, directory);
        
        return sb.ToString();
    }
    
    private void AppendDirectoryStructure(StringBuilder sb, DirectoryNode node, int depth)
    {
        string indent = new string(' ', depth * 2);
        
        // Add directory name
        if (depth > 0) // Skip for root directory
        {
            sb.AppendLine($"{indent}📁 {node.Name}/");
        }
        
        // Add documents in this directory
        foreach (var doc in node.Documents.OrderBy(d => d.FileName))
        {
            sb.AppendLine($"{indent}  📄 {doc.FileName}");
            
            // Add headers for each document, with proper indentation based on header level
            if (doc.Headers.Count > 0)
            {
                foreach (var header in doc.Headers.OrderBy(h => h.Line))
                {
                    string headerIndent = new string(' ', depth * 2 + 4 + header.Level - 1);
                    sb.AppendLine($"{headerIndent}• {header.Text}");
                }
            }
        }
        
        // Process subdirectories
        foreach (var subdir in node.Subdirectories.OrderBy(d => d.Name))
        {
            AppendDirectoryStructure(sb, subdir, depth + 1);
        }
    }
    
    private DocumentInfo? FindReadmeDocument(DirectoryNode node)
    {
        // First check in the current directory
        var readme = node.Documents.FirstOrDefault(d => 
            d.FileName.Equals("README.md", StringComparison.OrdinalIgnoreCase));
        
        if (readme != null)
        {
            return readme;
        }
        
        // Then check in subdirectories
        foreach (var subdir in node.Subdirectories)
        {
            readme = FindReadmeDocument(subdir);
            if (readme != null)
            {
                return readme;
            }
        }
        
        return null;
    }
    
    private async Task AppendSpecialFilesAsync(StringBuilder sb, IDirectoryInfo directory)
    {
        // Process special files
        foreach (var specialFile in _specialFiles)
        {
            string filePath = _fileSystem.Path.Combine(directory.FullName, specialFile.Key);
            if (_fileSystem.File.Exists(filePath))
            {
                sb.AppendLine();
                sb.AppendLine(specialFile.Value);
                sb.AppendLine();
                
                string fileContent = await _fileSystem.File.ReadAllTextAsync(filePath);
                sb.AppendLine(fileContent);
            }
        }
        
        // Check for Characters directory
        string charactersDirectoryPath = _fileSystem.Path.Combine(directory.FullName, "Characters");
        if (_fileSystem.Directory.Exists(charactersDirectoryPath))
        {
            sb.AppendLine();
            sb.AppendLine("# Character Files");
            sb.AppendLine();
            
            var charactersDirectory = _fileSystem.DirectoryInfo.FromDirectoryName(charactersDirectoryPath);
            var characterFiles = charactersDirectory.GetFiles()
                .Where(file => _fileSystem.Path.GetExtension(file.FullName).ToLowerInvariant() == ".md")
                .OrderBy(file => file.Name)
                .ToList();
                
            foreach (var characterFile in characterFiles)
            {
                sb.AppendLine($"## {_fileSystem.Path.GetFileNameWithoutExtension(characterFile.Name)}");
                string characterContent = await _fileSystem.File.ReadAllTextAsync(characterFile.FullName);
                sb.AppendLine(characterContent);
                sb.AppendLine();
            }
        }
    }
}
