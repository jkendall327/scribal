using System.ComponentModel;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Scribal.Cli;

public partial class DiffEditor(IFileSystem fileSystem, IConfiguration configuration)
{
    public const string DiffEditorToolName = "apply_diff";
    
    /// <summary>
    /// Applies a unified diff to a file
    /// </summary>
    /// <param name="file">The file path to apply the diff to</param>
    /// <param name="diff">The unified diff content as a string</param>
    /// <returns>The unified diff, for further inspection.</returns>
    [KernelFunction(DiffEditorToolName), Description("Applies an edit to a file.")]
    public async Task ApplyUnifiedDiffAsync(
        [Description("The path to the file to edit.")] string file, 
        [Description("The edit to apply, in unified diff format.")] string diff)
    {
        // Read the original file content
        var originalLines = await fileSystem.File.ReadAllLinesAsync(file);
        var newFileContent = new List<string>(originalLines);
            
        // Parse the diff
        var hunks = ParseUnifiedDiff(diff);
            
        // Apply hunks in reverse order to avoid line number changes affecting subsequent hunks
        for (var i = hunks.Count - 1; i >= 0; i--)
        {
            var hunk = hunks[i];
            ApplyHunk(newFileContent, hunk);
        }
            
        // Write the modified content back to the file
        var dry = configuration.GetValue<bool>("DryRun");
        
        if (!dry)
        {
            await fileSystem.File.WriteAllLinesAsync(file, newFileContent);
        }
    }
        
    private class DiffHunk
    {
        public int OriginalStart { get; set; }
        public int OriginalCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
        public List<string> Lines { get; set; } = [];
    }
        
    private List<DiffHunk> ParseUnifiedDiff(string diff)
    {
        var hunks = new List<DiffHunk>();
        DiffHunk? currentHunk = null;
            
        // Split the diff into lines
        var lines = diff.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            
        // Regular expression to match the hunk header
        var hunkHeaderRegex = HunkHeaderRegex();
            
        foreach (var line in lines)
        {
            // Skip file header lines (starting with ---, +++ or index)
            if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("index"))
            {
                continue;
            }
                
            // Check if this is a hunk header
            var match = hunkHeaderRegex.Match(line);
            if (match.Success)
            {
                // Create a new hunk
                currentHunk = new DiffHunk
                {
                    OriginalStart = int.Parse(match.Groups[1].Value),
                    OriginalCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                    NewStart = int.Parse(match.Groups[3].Value),
                    NewCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1
                };
                hunks.Add(currentHunk);
                continue;
            }
                
            // Add the line to the current hunk
            if (currentHunk != null)
            {
                currentHunk.Lines.Add(line);
            }
        }
            
        return hunks;
    }
        
    private void ApplyHunk(List<string> fileContent, DiffHunk hunk)
    {
        // Convert to 0-based indexing
        var lineIndex = hunk.OriginalStart - 1;
        var linesRemoved = 0;
        var linesToAdd = new List<string>();
            
        foreach (var line in hunk.Lines)
        {
            if (line.StartsWith("-"))
            {
                // Line to remove
                fileContent.RemoveAt(lineIndex);
                linesRemoved++;
            }
            else if (line.StartsWith("+"))
            {
                // Line to add
                linesToAdd.Add(line.Substring(1));
            }
            else if (line.StartsWith(" "))
            {
                // Context line - move to next line
                lineIndex++;
            }
            // Ignore other lines (like "\ No newline at end of file")
        }
            
        // Insert the new lines at the current position
        fileContent.InsertRange(lineIndex, linesToAdd);
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeaderRegex();
}