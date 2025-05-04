using System.IO.Abstractions;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Scribal.AI;

namespace Scribal.Cli;

public class PromptBuilder(
    IDocumentScanService scanService,
    PromptRenderer renderer,
    IFileSystem fileSystem,
    RepoMapStore store,
    IConfiguration config)
{
    private readonly Dictionary<string, string> _specialFiles = new()
    {
        {
            "PLOT.md", "# Plot Overview"
        },
        {
            "CHARACTERS.md", "# Characters"
        },
        {
            "STYLE.md", "# Style Guide"
        }
    };

    private string? _prefixedSystemPrompt;

    public async Task<string> BuildSystemPrompt(Kernel kernel)
    {
        if (!string.IsNullOrEmpty(_prefixedSystemPrompt))
        {
            return _prefixedSystemPrompt;
        }

        var prefix = config.GetValue<string>("SystemPromptPrefix");

        var request = new RenderRequest("System",
            "SystemPrompt",
            "Main prompt establishing the Scribal agent",
            new()
            {
                ["prefix"] = prefix
            });

        _prefixedSystemPrompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);

        return _prefixedSystemPrompt;
    }

    public async Task<string> BuildPromptAsync(IDirectoryInfo directory, string userInput)
    {
        try
        {
            var directoryTree = await scanService.ScanDirectoryForMarkdownAsync(directory);

            var sb = new StringBuilder();

            sb.AppendLine(
                "You are about to be asked a question or given a command by the user. Here is some information to help you.");

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

                var readmeContent = await fileSystem.File.ReadAllTextAsync(readmeDoc.FilePath);
                sb.AppendLine(readmeContent);
            }

            if (store.Paths.Any())
            {
                sb.AppendLine("---");
                sb.AppendLine("# Selected Files");
                sb.AppendLine("The user has selected to provide these files to you in full:");

                foreach (var path in store.Paths)
                {
                    var content = await fileSystem.File.ReadAllTextAsync(path);
                    var filename = fileSystem.Path.GetFileName(path);

                    sb.AppendLine("---");
                    sb.AppendLine(filename);
                    sb.AppendLine(content);
                    sb.AppendLine("---");
                }
            }

            // Add special files if they exist
            await AppendSpecialFilesAsync(sb, directory);

            sb.AppendLine("You have received all the necessary context to respond to the user. Here is their message:");
            sb.AppendLine(userInput);

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
        var indent = new string(' ', depth * 2);

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
            if (doc.Headers.Count <= 0) continue;

            foreach (var header in doc.Headers.OrderBy(h => h.Line))
            {
                var headerIndent = new string(' ', depth * 2 + 4 + header.Level - 1);
                sb.AppendLine($"{headerIndent}• {header.Text}");
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
            var filePath = fileSystem.Path.Combine(directory.FullName, specialFile.Key);
            if (fileSystem.File.Exists(filePath))
            {
                sb.AppendLine();
                sb.AppendLine(specialFile.Value);
                sb.AppendLine();

                var fileContent = await fileSystem.File.ReadAllTextAsync(filePath);
                sb.AppendLine(fileContent);
            }
        }

        // Check for Characters directory
        var charactersDirectoryPath = fileSystem.Path.Combine(directory.FullName, "Characters");
        if (fileSystem.Directory.Exists(charactersDirectoryPath))
        {
            sb.AppendLine();
            sb.AppendLine("# Character Files");
            sb.AppendLine();

            var charactersDirectory = fileSystem.DirectoryInfo.New(charactersDirectoryPath);

            var characterFiles = charactersDirectory.GetFiles()
                .Where(file =>
                    fileSystem.Path.GetExtension(file.FullName).Equals(".md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name)
                .ToList();

            foreach (var characterFile in characterFiles)
            {
                sb.AppendLine($"## {fileSystem.Path.GetFileNameWithoutExtension(characterFile.Name)}");
                var characterContent = await fileSystem.File.ReadAllTextAsync(characterFile.FullName);
                sb.AppendLine(characterContent);
                sb.AppendLine();
            }
        }
    }
}