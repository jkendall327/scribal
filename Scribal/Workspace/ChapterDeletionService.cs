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
    public List<string> ActionsTaken { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public Exception? Exception { get; init; }
}

public interface IChapterDeletionService
{
    Task<ChapterDeletionResult> DeleteChapterAsync(ChapterState chapterToDelete, CancellationToken cancellationToken);
}

public class ChapterDeletionService : IChapterDeletionService
{
    private const string PlotOutlineFileName = "plot_outline.json"; // Matches WorkspaceManager/ChapterManagerService
    private const string ChaptersDirectoryName = "chapters"; // Matches ChapterManagerService
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ChapterDeletionService> _logger;
    private readonly WorkspaceManager _workspaceManager;

    public ChapterDeletionService(IFileSystem fileSystem,
        WorkspaceManager workspaceManager,
        ILogger<ChapterDeletionService> logger)
    {
        _fileSystem = fileSystem;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<ChapterDeletionResult> DeleteChapterAsync(ChapterState chapterToDelete,
        CancellationToken cancellationToken)
    {
        var result = new ChapterDeletionResult
        {
            DeletedChapterTitle = chapterToDelete.Title,
            DeletedChapterOriginalNumber = chapterToDelete.Number
        };

        _logger.LogInformation("Attempting to delete Chapter {ChapterNumber}: {ChapterTitle}",
            chapterToDelete.Number,
            chapterToDelete.Title);

        var workspaceDir = WorkspaceManager.TryFindWorkspaceFolder(_fileSystem, _logger);

        if (string.IsNullOrEmpty(workspaceDir))
        {
            _logger.LogError("Workspace directory not found, aborting chapter deletion");
            result.Errors.Add("Could not find workspace directory. Cannot delete chapter.");

            return result with
            {
                Success = false,
                OverallMessage = "Failed to find workspace directory."
            };
        }

        var projectRootDir = _fileSystem.DirectoryInfo.New(workspaceDir).Parent?.FullName;

        if (string.IsNullOrEmpty(projectRootDir))
        {
            _logger.LogError("Project root directory not found, aborting chapter deletion");
            result.Errors.Add("Could not determine project root directory. Cannot delete chapter.");

            return result with
            {
                Success = false,
                OverallMessage = "Failed to determine project root directory."
            };
        }

        var mainChaptersDirPath = _fileSystem.Path.Join(projectRootDir, ChaptersDirectoryName);
        var plotOutlineFilePath = _fileSystem.Path.Join(workspaceDir, PlotOutlineFileName);

        try
        {
            // 1. Delete chapter subfolder
            var chapterDirName = $"chapter_{chapterToDelete.Number:D2}";
            var chapterDirPath = _fileSystem.Path.Join(mainChaptersDirPath, chapterDirName);

            if (_fileSystem.Directory.Exists(chapterDirPath))
            {
                _fileSystem.Directory.Delete(chapterDirPath, true);
                _logger.LogInformation("Deleted chapter directory: {ChapterDirectoryPath}", chapterDirPath);
                result.ActionsTaken.Add($"Deleted directory: {chapterDirPath}");
            }
            else
            {
                _logger.LogWarning("Chapter directory not found, skipping deletion: {ChapterDirectoryPath}",
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

            if (_fileSystem.File.Exists(plotOutlineFilePath))
            {
                var outlineJson = await _fileSystem.File.ReadAllTextAsync(plotOutlineFilePath, cancellationToken);

                storyOutline = JsonSerializer.Deserialize<StoryOutline>(outlineJson, JsonDefaults.Default);
            }

            storyOutline ??= new();

            if (storyOutline.Chapters == null)
            {
                storyOutline.Chapters = new();
            }

            var chapterToRemoveFromOutline =
                storyOutline.Chapters.FirstOrDefault(c => c.ChapterNumber == chapterToDelete.Number);

            List<(int OriginalNumber, Chapter ChapterRef)> originalChapterMap = [];

            if (chapterToRemoveFromOutline != null)
            {
                storyOutline.Chapters.Remove(chapterToRemoveFromOutline);

                _logger.LogInformation("Removed chapter {ChapterNumber} from StoryOutline object",
                    chapterToDelete.Number);
            }

            var newChapterNumber = 1;

            foreach (var ch in storyOutline.Chapters.OrderBy(c => c.ChapterNumber))
            {
                originalChapterMap.Add((ch.ChapterNumber, ch));
                ch.ChapterNumber = newChapterNumber++;
            }

            var updatedOutlineJson = JsonSerializer.Serialize(storyOutline, JsonDefaults.Default);

            await _fileSystem.File.WriteAllTextAsync(plotOutlineFilePath, updatedOutlineJson, cancellationToken);
            _logger.LogInformation("Updated and saved plot_outline.json");
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
            var workspaceState = await _workspaceManager.LoadWorkspaceStateAsync(workspaceDir, cancellationToken) ??
                                 new WorkspaceState();

            if (workspaceState.Chapters == null)
            {
                workspaceState.Chapters = new();
            }

            var chapterToRemoveFromState =
                workspaceState.Chapters.FirstOrDefault(cs => cs.Number == chapterToDelete.Number);

            if (chapterToRemoveFromState != null)
            {
                workspaceState.Chapters.Remove(chapterToRemoveFromState);

                _logger.LogInformation("Removed chapter {ChapterNumber} from WorkspaceState object",
                    chapterToDelete.Number);
            }

            newChapterNumber = 1;

            foreach (var cs in workspaceState.Chapters.OrderBy(c => c.Number))
            {
                cs.Number = newChapterNumber++;
            }

            await _workspaceManager.SaveWorkspaceStateAsync(workspaceState, workspaceDir, cancellationToken);
            _logger.LogInformation("Updated and saved workspace state");
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
            // AI: Changed OrderByDescending to OrderBy to ensure correct directory renaming order
            foreach ((var originalNum, var chapterRef) in originalChapterMap.OrderBy(m =>
                         m.ChapterRef.ChapterNumber))
            {
                var currentChapterNewNumber = chapterRef.ChapterNumber;

                if (originalNum != currentChapterNewNumber)
                {
                    var oldDirName = $"chapter_{originalNum:D2}";
                    var newDirName = $"chapter_{currentChapterNewNumber:D2}";
                    var oldPath = _fileSystem.Path.Join(mainChaptersDirPath, oldDirName);
                    var newPath = _fileSystem.Path.Join(mainChaptersDirPath, newDirName);

                    if (_fileSystem.Directory.Exists(oldPath) && oldPath != newPath)
                    {
                        _logger.LogInformation("Renaming chapter directory from {OldPath} to {NewPath}",
                            oldPath,
                            newPath);

                        _fileSystem.Directory.Move(oldPath, newPath);
                        result.ActionsTaken.Add($"Renamed directory: {oldDirName} -> {newDirName}");
                    }
                    else if (!_fileSystem.Directory.Exists(oldPath))
                    {
                        _logger.LogWarning("Expected old chapter directory {OldPath} not found for renaming", oldPath);
                        result.Warnings.Add($"Directory for renaming not found: {oldPath}");
                    }
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
            _logger.LogWarning(opEx,
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
            _logger.LogError(ex, "Failed to delete chapter {ChapterNumber}", chapterToDelete.Number);
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
