
using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public partial class ExportService(
    IFileSystem fileSystem,
    WorkspaceManager workspaceManager,
    IAnsiConsole console,
    ILogger<ExportService> logger)
{
    private const string DefaultExportFileName = "exported_story.md";
    private const string ChaptersDirectoryName = "chapters";
    private static readonly Regex DraftFileRegex = CreateDraftFileRegex();

    public async Task ExportStoryAsync(string? outputFileName, CancellationToken cancellationToken = default)
    {
        if (!workspaceManager.InWorkspace)
        {
            throw new InvalidOperationException("Attempted to export story when not in a workspace.");
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: cancellationToken);

        if (state is null || !state.Chapters.Any())
        {
            console.MarkupLine("[yellow]No chapters found in the workspace state to export.[/]");

            return;
        }

        var projectRootPath = fileSystem.DirectoryInfo.New(workspaceManager.CurrentWorkspacePath!).Parent?.FullName;

        if (string.IsNullOrEmpty(projectRootPath))
        {
            console.MarkupLine("[red]Could not determine project root path.[/]");

            logger.LogError("Could not determine project root path from workspace path {WorkspacePath}",
                workspaceManager.CurrentWorkspacePath);

            return;
        }

        var mainChaptersDirectoryPath = fileSystem.Path.Join(projectRootPath, ChaptersDirectoryName);

        if (!fileSystem.Directory.Exists(mainChaptersDirectoryPath))
        {            logger.LogInformation("Main chapters directory {Path} does not exist, creating it",
                mainChaptersDirectoryPath);

            fileSystem.Directory.CreateDirectory(mainChaptersDirectoryPath);
        }

        await ActuallyPerformExport(outputFileName, mainChaptersDirectoryPath, state, cancellationToken);
    }

    private async Task ActuallyPerformExport(string? outputFileName,
        string mainChaptersDirectoryPath,
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        var storyContentBuilder = new StringBuilder();

        logger.LogInformation("Starting export process for {ChapterCount} chapters", state.Chapters.Count);

        foreach (var chapterState in state.Chapters.OrderBy(c => c.Number))
        {
            await GetChapterContent(mainChaptersDirectoryPath, chapterState, storyContentBuilder, cancellationToken);
        }

        var outputFilePath = GetOutputPath(outputFileName, mainChaptersDirectoryPath);

        if (storyContentBuilder.Length > 0)
        {
            await fileSystem.File.WriteAllTextAsync(outputFilePath, storyContentBuilder.ToString(), cancellationToken);
            console.MarkupLine($"[green]Story successfully exported to:[/] {outputFilePath}");
            logger.LogInformation("Story exported to {FilePath}", outputFilePath);
        }
        else
        {
            console.MarkupLine(
                "[yellow]No content was exported. All chapters might be missing content or directories.[/]");

            logger.LogWarning("Export finished, but no content was generated");
        }
    }

    private async Task GetChapterContent(string mainChaptersDirectoryPath,
        ChapterState chapterState,
        StringBuilder storyContentBuilder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chapterDirectoryName = $"chapter_{chapterState.Number:D2}";
        var chapterSpecificDirectoryPath = fileSystem.Path.Join(mainChaptersDirectoryPath, chapterDirectoryName);

        if (!fileSystem.Directory.Exists(chapterSpecificDirectoryPath))
        {
            logger.LogWarning("Chapter directory {Path} not found for chapter {Number} - {Title}, skipping",
                chapterSpecificDirectoryPath,
                chapterState.Number,
                chapterState.Title);

            return;
        }

        var chapterFiles = fileSystem.Directory.GetFiles(chapterSpecificDirectoryPath, "*.md");
        string? latestDraftPath = null;

        // Prioritize final files
        var finalFiles = chapterFiles
                         .Where(f => fileSystem.Path.GetFileNameWithoutExtension(f)
                                               .EndsWith("_final", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(f =>
                             fileSystem.FileInfo.New(f).LastWriteTimeUtc)                         .ToList();

        if (finalFiles.Any())
        {
            latestDraftPath = finalFiles.First();
            logger.LogDebug("Found final file {FilePath} for chapter {Number}", latestDraftPath, chapterState.Number);
        }
        else
        {
            // If no final file, look for the highest numbered draft
            var draftFiles = chapterFiles.Select(f => new
                                         {
                                             Path = f,
                                             Match = DraftFileRegex.Match(
                                                 fileSystem.Path.GetFileNameWithoutExtension(f))
                                         })
                                         .Where(x => x.Match.Success)
                                         .Select(x => new
                                         {
                                             x.Path,
                                             DraftNumber = int.Parse(x.Match.Groups[1].Value),
                                             LastWriteTime = fileSystem.FileInfo.New(x.Path).LastWriteTimeUtc
                                         })
                                         .OrderByDescending(x => x.DraftNumber)
                                         .ThenByDescending(x =>
                                             x.LastWriteTime)                                         .ToList();

            if (draftFiles.Any())
            {
                latestDraftPath = draftFiles.First().Path;

                logger.LogDebug("Found latest draft file {FilePath} for chapter {Number}",
                    latestDraftPath,
                    chapterState.Number);
            }
        }

        if (latestDraftPath is not null)
        {
            var chapterContent = await fileSystem.File.ReadAllTextAsync(latestDraftPath, cancellationToken);
            storyContentBuilder.AppendLine($"# {chapterState.Title}");
            storyContentBuilder.AppendLine();
            storyContentBuilder.AppendLine(chapterContent.Trim());
            storyContentBuilder.AppendLine();
            storyContentBuilder.AppendLine("---");            storyContentBuilder.AppendLine();
        }
        else
        {
            logger.LogWarning("No suitable draft or final file found for chapter {Number} - {Title} in {Path}",
                chapterState.Number,
                chapterState.Title,
                chapterSpecificDirectoryPath);

            // Add a placeholder for missing chapters
            storyContentBuilder.AppendLine($"# {chapterState.Title}");
            storyContentBuilder.AppendLine();

            storyContentBuilder.AppendLine(
                $"*Content for chapter {chapterState.Number} ({chapterState.Title}) not found.*");

            storyContentBuilder.AppendLine();
            storyContentBuilder.AppendLine("---");
            storyContentBuilder.AppendLine();
        }
    }

    private string GetOutputPath(string? outputFileName, string mainChaptersDirectoryPath)
    {
        var finalOutputFileName = string.IsNullOrWhiteSpace(outputFileName) ? DefaultExportFileName : outputFileName;

        if (!finalOutputFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            finalOutputFileName += ".md";
        }

        var outputFilePath = fileSystem.Path.Join(mainChaptersDirectoryPath, finalOutputFileName);

        return outputFilePath;
    }

    [GeneratedRegex(@"_draft(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-GB")]
    private static partial Regex CreateDraftFileRegex();
}