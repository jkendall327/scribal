using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Scribal.Workspace;

public record ChapterDeletionResult
{
    public bool Success { get; init; }
    public string? OverallMessage { get; init; }
    public string? DeletedChapterTitle { get; init; }
    public int? DeletedChapterOriginalNumber { get; init; }
    public List<string> ActionsTaken { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public Exception? Exception { get; init; }
}

public interface IChapterDeletionService
{
    Task<ChapterDeletionResult> DeleteChapterAsync(ChapterState chapterToDelete, CancellationToken cancellationToken);
}

public class ChapterDeletionService(
    IFileSystem fileSystem,
    WorkspaceManager workspaceManager,
    ILogger<ChapterDeletionService> logger) : IChapterDeletionService
{
    private const string PlotOutlineFileName = "plot_outline.json"; // Matches WorkspaceManager/ChapterManagerService
    private const string ChaptersDirectoryName = "chapters"; // Matches ChapterManagerService

    public async Task<ChapterDeletionResult> DeleteChapterAsync(ChapterState chapterToDelete,
        CancellationToken cancellationToken)
    {
        var result = new ChapterDeletionResult
        {
            DeletedChapterTitle = chapterToDelete.Title,
            DeletedChapterOriginalNumber = chapterToDelete.Number
        };

        logger.LogInformation("Attempting to delete Chapter {ChapterNumber}: {ChapterTitle}",
            chapterToDelete.Number,
            chapterToDelete.Title);

        var workspaceDir = WorkspaceManager.TryFindWorkspaceFolder(fileSystem, logger);

        if (string.IsNullOrEmpty(workspaceDir))
        {
            logger.LogError("Workspace directory not found, aborting chapter deletion");
            result.Errors.Add("Could not find workspace directory. Cannot delete chapter.");

            return result with
            {
                Success = false,
                OverallMessage = "Failed to find workspace directory."
            };
        }

        var projectRootDir = fileSystem.DirectoryInfo.New(workspaceDir).Parent?.FullName;

        if (string.IsNullOrEmpty(projectRootDir))
        {
            logger.LogError("Project root directory not found, aborting chapter deletion");
            result.Errors.Add("Could not determine project root directory. Cannot delete chapter.");

            return result with
            {
                Success = false,
                OverallMessage = "Failed to determine project root directory."
            };
        }

        var mainChaptersDirPath = fileSystem.Path.Join(projectRootDir, ChaptersDirectoryName);
        var plotOutlineFilePath = fileSystem.Path.Join(workspaceDir, PlotOutlineFileName);

        try
        {
            // 1. Delete chapter subfolder
            var chapterDirName = $"chapter_{chapterToDelete.Number:D2}";
            var chapterDirPath = fileSystem.Path.Join(mainChaptersDirPath, chapterDirName);

            if (fileSystem.Directory.Exists(chapterDirPath))
            {
                fileSystem.Directory.Delete(chapterDirPath, true);
                logger.LogInformation("Deleted chapter directory: {ChapterDirectoryPath}", chapterDirPath);
                result.ActionsTaken.Add($"Deleted directory: {chapterDirPath}");
            }
            else
            {
                logger.LogWarning("Chapter directory not found, skipping deletion: {ChapterDirectoryPath}",
                    chapterDirPath);

                result.Warnings.Add($"Directory not found, skipped deletion: {chapterDirPath}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return result with
                {
                    Success = false,
                    OverallMessage = "Operation cancelled."
                };
            }

            // 2. Update StoryOutline
            StoryOutline? storyOutline = null;

            if (fileSystem.File.Exists(plotOutlineFilePath))
            {
                var outlineJson = await fileSystem.File.ReadAllTextAsync(plotOutlineFilePath, cancellationToken);

                storyOutline = JsonSerializer.Deserialize<StoryOutline>(outlineJson, JsonDefaults.Default);
            }

            storyOutline ??= new();

            var chapterToRemoveFromOutline =
                storyOutline.Chapters.FirstOrDefault(c => c.ChapterNumber == chapterToDelete.Number);

            List<(int OriginalNumber, Chapter ChapterRef)> originalChapterMap = [];

            if (chapterToRemoveFromOutline != null)
            {
                storyOutline.Chapters.Remove(chapterToRemoveFromOutline);

                logger.LogInformation("Removed chapter {ChapterNumber} from StoryOutline object",
                    chapterToDelete.Number);
            }

            var newChapterNumber = 1;

            foreach (var ch in storyOutline.Chapters.OrderBy(c => c.ChapterNumber))
            {
                originalChapterMap.Add((ch.ChapterNumber, ch));
                ch.ChapterNumber = newChapterNumber++;
            }

            var updatedOutlineJson = JsonSerializer.Serialize(storyOutline, JsonDefaults.Default);

            await fileSystem.File.WriteAllTextAsync(plotOutlineFilePath, updatedOutlineJson, cancellationToken);
            logger.LogInformation("Updated and saved plot_outline.json");
            result.ActionsTaken.Add("Plot outline updated.");

            if (cancellationToken.IsCancellationRequested)
            {
                return result with
                {
                    Success = false,
                    OverallMessage = "Operation cancelled."
                };
            }

            // 3. Update WorkspaceState
            var workspaceState = await workspaceManager.LoadWorkspaceStateAsync(workspaceDir, cancellationToken) ??
                                 new WorkspaceState();

            var chapterToRemoveFromState =
                workspaceState.Chapters.FirstOrDefault(cs => cs.Number == chapterToDelete.Number);

            if (chapterToRemoveFromState != null)
            {
                workspaceState.Chapters.Remove(chapterToRemoveFromState);

                logger.LogInformation("Removed chapter {ChapterNumber} from WorkspaceState object",
                    chapterToDelete.Number);
            }

            newChapterNumber = 1;

            foreach (var cs in workspaceState.Chapters.OrderBy(c => c.Number))
            {
                cs.Number = newChapterNumber++;
            }

            await workspaceManager.SaveWorkspaceStateAsync(workspaceState, workspaceDir, cancellationToken);
            logger.LogInformation("Updated and saved workspace state");
            result.ActionsTaken.Add("Workspace state updated.");

            if (cancellationToken.IsCancellationRequested)
            {
                return result with
                {
                    Success = false,
                    OverallMessage = "Operation cancelled."
                };
            }

            // 4. Rename remaining chapter subfolders
            // We use OrderBy to ensure correct directory renaming order.
            foreach ((var originalNum, var chapterRef) in originalChapterMap.OrderBy(m => m.ChapterRef.ChapterNumber))
            {
                var currentChapterNewNumber = chapterRef.ChapterNumber;

                if (originalNum == currentChapterNewNumber)
                {
                    continue;
                }

                var oldDirName = $"chapter_{originalNum:D2}";
                var newDirName = $"chapter_{currentChapterNewNumber:D2}";
                var oldPath = fileSystem.Path.Join(mainChaptersDirPath, oldDirName);
                var newPath = fileSystem.Path.Join(mainChaptersDirPath, newDirName);

                if (fileSystem.Directory.Exists(oldPath) && oldPath != newPath)
                {
                    logger.LogInformation("Renaming chapter directory from {OldPath} to {NewPath}",
                        oldPath,
                        newPath);

                    fileSystem.Directory.Move(oldPath, newPath);
                    result.ActionsTaken.Add($"Renamed directory: {oldDirName} -> {newDirName}");
                }
                else if (!fileSystem.Directory.Exists(oldPath))
                {
                    logger.LogWarning("Expected old chapter directory {OldPath} not found for renaming", oldPath);
                    result.Warnings.Add($"Directory for renaming not found: {oldPath}");
                }
            }

            result.ActionsTaken.Add("Chapter directories re-organized.");

            return result with
            {
                Success = true,
                OverallMessage = $"Chapter {chapterToDelete.Number}: '{chapterToDelete.Title
                }' successfully deleted and workspace updated."
            };
        }
        catch (OperationCanceledException opEx)
        {
            logger.LogWarning(opEx,
                "Chapter deletion operation was cancelled for Chapter {ChapterNumber}",
                chapterToDelete.Number);

            result.Errors.Add("Operation cancelled during execution.");

            return result with
            {
                Success = false,
                Exception = opEx,
                OverallMessage = "Chapter deletion cancelled."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete chapter {ChapterNumber}", chapterToDelete.Number);
            result.Errors.Add($"An error occurred: {ex.Message}");

            return result with
            {
                Success = false,
                Exception = ex,
                OverallMessage = "An error occurred while deleting the chapter."
            };
        }
    }
}