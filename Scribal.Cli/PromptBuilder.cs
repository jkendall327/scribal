using System.IO.Abstractions;
using System.Text;

namespace Scribal.Cli;

public class PromptBuilder
{
    private const string SYSTEM_PROMPT = @"# Scribal - Fiction Writing Assistant

You are an expert fiction writer, editor, and creative consultant. Your purpose is to help the user with their fiction writing project.

## Your Capabilities
- Draft new scenes based on the project's existing structure and style
- Revise and improve existing scenes
- Provide feedback on character development, plot coherence, and pacing
- Suggest new plot directions or character arcs
- Help maintain consistency with established world-building and character traits
- Adapt your writing style to match the project's tone and voice

## Your Approach
- Be constructive and supportive in your feedback
- Respect the user's creative vision while offering improvements
- Consider the project's genre conventions and target audience
- Maintain continuity with existing content
- Provide specific, actionable suggestions rather than vague advice

Use the following project information to inform your assistance:
";

    private readonly IDocumentScanService _scanService;
    private readonly IFileSystem _fileSystem;
    private readonly RepoMapStore _store;
    
    private readonly Dictionary<string, string> _specialFiles = new()
    {
        { "PLOT.md", "# Plot Overview" },
        { "CHARACTERS.md", "# Characters" },
        { "STYLE.md", "# Style Guide" }
    };

    public PromptBuilder(IDocumentScanService scanService, IFileSystem fileSystem, RepoMapStore store)
    {
        _scanService = scanService;
        _fileSystem = fileSystem;
        _store = store;
    }

    public async Task<string> BuildPromptAsync(IDirectoryInfo directory)
    {
        try
        {
            var directoryTree = await _scanService.ScanDirectoryForMarkdownAsync(directory);
            
            var sb = new StringBuilder();
        
            // Add the system prompt first
            sb.AppendLine(SYSTEM_PROMPT);
        
            sb.AppendLine("# Project Structure");
            sb.AppendLine("The following is a map of markdown documents in this project:");
            sb.AppendLine();

            AppendDirectoryMap(sb, directoryTree, 0);
        
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

            if (_store.Paths.Any())
            {
                sb.AppendLine("---");
                sb.AppendLine("# Selected Files");
                sb.AppendLine("The user has selected to provide these files to you in full:");
                
                foreach (var path in _store.Paths)
                {
                    var content = await _fileSystem.File.ReadAllTextAsync(path);
                    var filename = _fileSystem.Path.GetFileName(path);
                    
                    sb.AppendLine("---");
                    sb.AppendLine(filename);
                    sb.AppendLine(content);
                    sb.AppendLine("---");
                }
            }
        
            // Add special files if they exist
            await AppendSpecialFilesAsync(sb, directory);
        
            return sb.ToString();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        

    }
    
    private void AppendDirectoryMap(StringBuilder sb, DirectoryNode node, int depth)
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
            AppendDirectoryMap(sb, subdir, depth + 1);
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
