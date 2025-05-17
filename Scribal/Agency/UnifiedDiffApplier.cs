using System.Text.RegularExpressions;

namespace Scribal.Agency;

// AI: New class to encapsulate the logic for applying unified diffs.
public static partial class UnifiedDiffApplier
{
    // AI: Moved from DiffEditor.cs
    public static List<string> ApplyUnifiedDiffInner(IEnumerable<string> originalLines, string diff)
    {
        var newFileContent = new List<string>(originalLines);

        // Parse the diff
        var hunks = ParseUnifiedDiff(diff);

        // Apply hunks in reverse order to avoid line number changes affecting subsequent hunks
        for (var i = hunks.Count - 1; i >= 0; i--)
        {
            var hunk = hunks[i];
            ApplyHunk(newFileContent, hunk);
        }

        return newFileContent;
    }

    // AI: Moved from DiffEditor.cs
    private sealed class DiffHunk
    {
        public int OriginalStart { get; init; }
        public int OriginalCount { get; init; }
        public int NewStart { get; init; }
        public int NewCount { get; init; }
        public List<string> Lines { get; } = [];
    }

    // AI: Moved from DiffEditor.cs
    private static List<DiffHunk> ParseUnifiedDiff(string diff)
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
                currentHunk = new()
                {
                    OriginalStart = int.Parse(match.Groups[1].Value),
                    OriginalCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                    NewStart = int.Parse(match.Groups[3].Value),
                    NewCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1
                };

                hunks.Add(currentHunk);

                continue;
            }

            if (currentHunk == null)
            {
                continue;
            }

            // Add the line to the current hunk
            // Only add lines that are part of the hunk body (context, add, remove)
            // This also implicitly ignores "\ No newline at end of file"
            if (line.StartsWith(' ') || line.StartsWith('+') || line.StartsWith('-'))
            {
                currentHunk.Lines.Add(line);
            }
        }

        return hunks;
    }

    // AI: Moved from DiffEditor.cs
    private static void ApplyHunk(List<string> fileContent, DiffHunk hunk)
    {
        // currentPositionInFile is the 0-based index in fileContent where the hunk's changes start.
        // If OriginalStart is 1, changes apply at index 0.
        // If OriginalStart is 0 (e.g., @@ -0,0 +1,N @@ for adding to empty file),
        // operations effectively start "before" the first line, so insertions go at index 0.
        var currentPositionInFile = hunk.OriginalStart == 0 ? 0 : hunk.OriginalStart - 1;

        foreach (var lineContentFromDiff in hunk.Lines)
        {
            var operation = lineContentFromDiff[0];
            var lineData = lineContentFromDiff[1..];

            switch (operation)
            {
                // Context line
                case ' ' when currentPositionInFile >= fileContent.Count ||
                              fileContent[currentPositionInFile] != lineData:
                {
                    throw new InvalidOperationException(
                        $"Context mismatch at line {currentPositionInFile + 1}. Expected: '{lineData}', Actual: '{(currentPositionInFile < fileContent.Count
                            ? fileContent[currentPositionInFile]
                            : "Out of bounds/End of file")}'. Hunk: @@ -{hunk.OriginalStart},{hunk.OriginalCount} +{hunk.NewStart},{hunk.NewCount} @@");
                }
                case ' ':
                {
                    currentPositionInFile++;

                    break;
                }

                // Deletion line
                case '-' when currentPositionInFile >= fileContent.Count ||
                              fileContent[currentPositionInFile] != lineData:
                {
                    throw new InvalidOperationException(
                        $"Deletion mismatch at line {currentPositionInFile + 1}. Expected to delete: '{lineData}', Actual: '{(currentPositionInFile < fileContent.Count
                            ? fileContent[currentPositionInFile]
                            : "Out of bounds/End of file")}'. Hunk: @@ -{hunk.OriginalStart},{hunk.OriginalCount} +{hunk.NewStart},{hunk.NewCount} @@");
                }

                // Do not increment currentPositionInFile, as the next line shifts up.
                case '-':
                {
                    fileContent.RemoveAt(currentPositionInFile);

                    break;
                }

                // Addition line
                // Ensure insertion happens within valid bounds (at Count is okay for end-of-list)
                case '+' when currentPositionInFile > fileContent.Count:
                {
                    throw new InvalidOperationException(
                        $"Attempting to insert line at an invalid position {currentPositionInFile} (file size {fileContent.Count}). Hunk: @@ -{hunk.OriginalStart},{hunk.OriginalCount} +{hunk.NewStart},{hunk.NewCount} @@");
                }
                case '+':
                {
                    fileContent.Insert(currentPositionInFile, lineData);
                    currentPositionInFile++; // Increment because we've added a line.

                    break;
                }
            }

            // Other lines (like "\ No newline at end of file") are already filtered by ParseUnifiedDiff or ignored here.
        }
    }

    // AI: Moved from DiffEditor.cs and made static
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeaderRegex();
}
