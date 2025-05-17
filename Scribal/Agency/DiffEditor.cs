using System.ComponentModel;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Scribal.Config;

namespace Scribal.Agency;

public class DiffEditor(
    IFileSystem fileSystem,
    FileAccessChecker checker,
    IOptions<AppConfig> options,
    ILogger<DiffEditor> logger)
{
    public const string DiffEditorToolName = "apply_diff";

    /// <summary>
    ///     Applies a unified diff to a file
    /// </summary>
    /// <param name="file">The file path to apply the diff to</param>
    /// <param name="diff">The unified diff content as a string</param>
    /// <returns>The unified diff, for further inspection.</returns>
    [KernelFunction(DiffEditorToolName)]
    [Description("Applies an edit to a file.")]
    public async Task<string> ApplyUnifiedDiffAsync([Description("The path to the file to edit.")] string file,
        [Description("The edit to apply, in unified diff format.")] string diff)
    {
        if (!fileSystem.File.Exists(file))
        {
            logger.LogWarning("File not found at path {FilePath}", file);

            return FileAccessChecker.FileNotFoundError;
        }

        var ok = checker.IsInCurrentWorkingDirectory(file);

        if (!ok)
        {
            logger.LogWarning("Access denied for file {FilePath} as it is outside the current working directory", file);

            return FileAccessChecker.AccessDeniedError;
        }

        // Read the original file content
        var originalLines = await fileSystem.File.ReadAllLinesAsync(file);
        var newFileContent = UnifiedDiffApplier.ApplyUnifiedDiffInner(originalLines, diff);

        // Write the modified content back to the file
        if (!options.Value.DryRun)
        {
            await fileSystem.File.WriteAllLinesAsync(file, newFileContent);
        }

        return "File edited successfully.";
    }
}