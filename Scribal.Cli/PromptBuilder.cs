using System.Text;

namespace Scribal.Cli;

public class PromptBuilder(IDocumentScanService scanService)
{
    public async Task<string> BuildPromptAsync()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var directoryTree = await scanService.ScanDirectoryForMarkdownAsync(currentDirectory);
        
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("# Project Structure");
        stringBuilder.AppendLine("The following is a map of markdown documents in this project:");
        stringBuilder.AppendLine();
        
        AppendDirectoryStructure(stringBuilder, directoryTree, 0);
        
        // Add README content if available
        var readmeDoc = FindReadmeDocument(directoryTree);
        if (readmeDoc != null)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("# Project README");
            stringBuilder.AppendLine();
            
            string readmeContent = await File.ReadAllTextAsync(readmeDoc.FilePath);
            stringBuilder.AppendLine(readmeContent);
        }
        
        return stringBuilder.ToString();
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
}
