using System.IO.Abstractions;
using System.Reflection;
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

    private string? _mainPromptTemplate;

    private async Task<string> LoadTemplateAsync(string templateName)
    {
        var location = Assembly.GetExecutingAssembly().Location;

        var contentRoot = fileSystem.Path.GetDirectoryName(location);

        var templatePath = fileSystem.Path.Combine(contentRoot, "Prompts", $"{templateName}.hbs");
        if (!fileSystem.File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template file not found: {templatePath}");
        }

        return await fileSystem.File.ReadAllTextAsync(templatePath);
    }

    public async Task<string> BuildPromptAsync(Kernel kernel, IDirectoryInfo directory, string userInput)
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
                selectedFiles.Add(new FileContent
                {
                    FileName = filename,
                    Content = content
                });
            }

            // Get special files
            var specialFileContents = await GetSpecialFilesAsync(directory);

            // Get character files
            var characterFiles = await GetCharacterFilesAsync(directory);

            // Define the main prompt template
            const string promptTemplate =
                @"You are about to be asked a question or given a command by the user. Here is some information to help you.

# Project Structure
The following is a map of markdown documents in this project:

{{{DirectoryMap}}}

{{#if ReadmeContent}}
# Project README

{{ReadmeContent}}
{{/if}}

{{#if SelectedFiles}}
---
# Selected Files
The user has selected to provide these files to you in full:

{{#each SelectedFiles}}
---
{{FileName}}
{{Content}}
---
{{/each}}
{{/if}}

{{#if SpecialFiles}}
{{#each SpecialFiles}}
{{Title}}

{{Content}}
{{/each}}
{{/if}}

{{#if CharacterFiles}}
# Character Files

{{#each CharacterFiles}}
## {{FileName}}
{{Content}}

{{/each}}
{{/if}}

You have received all the necessary context to respond to the user. Here is their message:
{{UserInput}}";

            // Create prompt template configuration
            var promptConfig = new PromptTemplateConfig
            {
                Template = promptTemplate,
                TemplateFormat = "handlebars"
            };

            var template = _templateFactory.Create(promptConfig);
            
            // Render the template with the data model
            return await template.RenderAsync(kernel, new()
            {
                { "DirectoryMap", directoryMap },
                { "ReadmeContent", readmeContent },
                { "SelectedFiles", selectedFiles },
                { "SpecialFiles", specialFileContents },
                { "CharacterFiles", characterFiles },
                { "UserInput", userInput },
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    private readonly HandlebarsPromptTemplateFactory _templateFactory = new();

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
            if (fileSystem.File.Exists(filePath))
            {
                var fileContent = await fileSystem.File.ReadAllTextAsync(filePath);
                result.Add(new SpecialFileContent
                {
                    Title = specialFile.Value,
                    Content = fileContent
                });
            }
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
            result.Add(new FileContent
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