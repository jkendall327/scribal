using System.IO.Abstractions;
using System.Text;
using HandlebarsDotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;
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

    public async Task<string> BuildContextPrimerAsync(Kernel kernel, IDirectoryInfo directory, string userInput)
    {
        try
        {
            // Scan the directory
            var directoryTree = await scanService.ScanDirectoryForMarkdownAsync(directory);

            // Pre-format the directory map as a string
            var directoryMapBuilder = new StringBuilder();
            FormatDirectoryMap(directoryMapBuilder, directoryTree, 0);
            var directoryMap = directoryMapBuilder.ToString();

            // Find README document
            var readmeDoc = FindReadmeDocument(directoryTree);
            string? readmeContent = null;
            if (readmeDoc != null)
            {
                readmeContent = await fileSystem.File.ReadAllTextAsync(readmeDoc.FilePath);
            }

            // Get selected file contents
            var selectedFiles = new List<FileContent>();
            foreach (var path in store.Paths)
            {
                var content = await fileSystem.File.ReadAllTextAsync(path);
                var filename = fileSystem.Path.GetFileName(path);
                selectedFiles.Add(new()
                {
                    FileName = filename,
                    Content = content
                });
            }

            // Get special files
            var specialFileContents = await GetSpecialFilesAsync(directory);

            // Get character files
            var characterFiles = await GetCharacterFilesAsync(directory);
            
            var kernelArgs = new KernelArguments
            {
                {
                    "DirectoryMap", directoryMap
                },
                {
                    "ReadmeContent", readmeContent
                },
                {
                    "SelectedFiles", selectedFiles
                },
                {
                    "SpecialFiles", specialFileContents
                },
                {
                    "CharacterFiles", characterFiles
                },
                {
                    "UserInput", userInput
                },
            };

            var request = new RenderRequest("Primer", "Primer", "Main priming prompt", kernelArgs);
            return await renderer.RenderPromptTemplateFromFileAsync(kernel, request);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void FormatDirectoryMap(StringBuilder sb, DirectoryNode node, int depth)
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
            if (doc.Headers.Count <= 0)
            {
                continue;
            }

            foreach (var header in doc.Headers.OrderBy(h => h.Line))
            {
                var headerIndent = new string(' ', depth * 2 + 4 + header.Level - 1);
                sb.AppendLine($"{headerIndent}• {header.Text}");
            }
        }

        // Process subdirectories
        foreach (var subdir in node.Subdirectories.OrderBy(d => d.Name))
        {
            FormatDirectoryMap(sb, subdir, depth + 1);
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

    private async Task<List<SpecialFileContent>> GetSpecialFilesAsync(IDirectoryInfo directory)
    {
        var result = new List<SpecialFileContent>();

        // Process special files
        foreach (var specialFile in _specialFiles)
        {
            var filePath = fileSystem.Path.Combine(directory.FullName, specialFile.Key);
            
            if (!fileSystem.File.Exists(filePath))
            {
                continue;
            }

            var fileContent = await fileSystem.File.ReadAllTextAsync(filePath);
            
            result.Add(new()
            {
                Title = specialFile.Value,
                Content = fileContent
            });
        }

        return result;
    }

    private async Task<List<FileContent>> GetCharacterFilesAsync(IDirectoryInfo directory)
    {
        var result = new List<FileContent>();

        // Check for Characters directory
        var charactersDirectoryPath = fileSystem.Path.Combine(directory.FullName, "Characters");
        
        if (!fileSystem.Directory.Exists(charactersDirectoryPath))
        {
            return result;
        }

        var charactersDirectory = fileSystem.DirectoryInfo.New(charactersDirectoryPath);

        var characterFiles = charactersDirectory.GetFiles()
            .Where(file =>
                fileSystem.Path.GetExtension(file.FullName).Equals(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name)
            .ToList();

        foreach (var characterFile in characterFiles)
        {
            var characterContent = await fileSystem.File.ReadAllTextAsync(characterFile.FullName);
            result.Add(new()
            {
                FileName = fileSystem.Path.GetFileNameWithoutExtension(characterFile.Name),
                Content = characterContent
            });
        }

        return result;
    }
}

public class FileContent
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class SpecialFileContent
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}