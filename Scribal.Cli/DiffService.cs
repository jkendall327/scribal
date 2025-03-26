using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Scribal.Cli
{
    public class DiffService
    {
        /// <summary>
        /// Applies a unified diff to a file
        /// </summary>
        /// <param name="file">The file path to apply the diff to</param>
        /// <param name="diff">The unified diff content as a string</param>
        /// <returns>A task representing the asynchronous operation</returns>
        [Description("Applies an edit to a file. Provide the file name and the changes in unified diff format.")]
        public async Task ApplyUnifiedDiffAsync(string file, string diff)
        {
            // Read the original file content
            string[] originalLines = await File.ReadAllLinesAsync(file);
            List<string> newFileContent = new List<string>(originalLines);
            
            // Parse the diff
            var hunks = ParseUnifiedDiff(diff);
            
            // Apply hunks in reverse order to avoid line number changes affecting subsequent hunks
            for (int i = hunks.Count - 1; i >= 0; i--)
            {
                var hunk = hunks[i];
                ApplyHunk(newFileContent, hunk);
            }
            
            // Write the modified content back to the file
            await File.WriteAllLinesAsync(file, newFileContent);
        }
        
        private class DiffHunk
        {
            public int OriginalStart { get; set; }
            public int OriginalCount { get; set; }
            public int NewStart { get; set; }
            public int NewCount { get; set; }
            public List<string> Lines { get; set; } = new List<string>();
        }
        
        private List<DiffHunk> ParseUnifiedDiff(string diff)
        {
            List<DiffHunk> hunks = new List<DiffHunk>();
            DiffHunk? currentHunk = null;
            
            // Split the diff into lines
            string[] lines = diff.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            // Regular expression to match the hunk header
            var hunkHeaderRegex = new Regex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
            
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
            int lineIndex = hunk.OriginalStart - 1;
            int linesRemoved = 0;
            List<string> linesToAdd = new List<string>();
            
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
    }
}
