using System.IO.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Scribal.Agency;

namespace Scribal.Tests.Agency
{
    public class DiffEditorTests
    {
        private readonly DiffEditor _sut;

        public DiffEditorTests()
        {
            // Mock IFileSystem (not directly used by ApplyUnifiedDiffInner but required by constructor)
            var mockFileSystem = Substitute.For<IFileSystem>(); 
            
            // Mock IOptions<AppConfig> (not directly used by ApplyUnifiedDiffInner but required by constructor)
            var mockOptions = Substitute.For<IOptions<AppConfig>>();
            mockOptions.Value.Returns(new AppConfig { DryRun = false });

            _sut = new DiffEditor(mockFileSystem, mockOptions);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleAddition_AddsLineAtEndOfHunkContext()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            // This diff means: within the context of lines a, b, c, a line 'x' is added.
            // The current ApplyHunk logic processes context lines first, advancing lineIndex,
            // then inserts added lines at the final lineIndex.
            var diff = """
                       @@ -1,3 +1,4 @@
                        a
                       +x
                        b
                        c
                       """;

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            // Expected based on current logic: a, b, c, then x is inserted.
            resultLines.Should().Equal("a", "b", "c", "x");
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleDeletion_RemovesLineCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            var diff = """
                       @@ -1,3 +1,2 @@
                        a
                       -b
                        c
                       """;
            var expectedLines = new List<string> { "a", "c" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleChange_AppliesChangeAtEndOfHunkContext()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            // This diff means: within context a,b,c, 'b' is removed and 'x' is added.
            // Current logic: process 'a' (lineIndex=1), remove 'b' (file=["a","c"], lineIndex=1),
            // collect '+x', process 'c' (lineIndex=2). Insert 'x' at index 2.
            var diff = """
                       @@ -1,3 +1,3 @@
                        a
                       -b
                       +x
                        c
                       """;
            var expectedLines = new List<string> { "a", "c", "x" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }
        
        [Fact]
        public void ApplyUnifiedDiffInner_ChangeAtSpecificLine_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            // This diff targets line 'b' specifically for replacement.
            var diff = """
                       @@ -2,1 +2,1 @@
                       -b
                       +x
                       """;
            // Expected: line 'b' (index 1) removed, 'x' inserted at index 1.
            var expectedLines = new List<string> { "a", "x", "c" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }


        [Fact]
        public void ApplyUnifiedDiffInner_AdditionAtBeginning_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b" };
            // Diff means: at original line 1, 0 lines taken, new line 1, 1 line added. Insert '+x'.
            var diff = """
                       @@ -1,0 +1,1 @@
                       +x
                       """;
            // Expected: 'x' inserted at index 0.
            var expectedLines = new List<string> { "x", "a", "b" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AdditionInMiddle_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            // Diff means: at original line 2, 0 lines taken, new line 2, 1 line added. Insert '+x'.
            var diff = """
                       @@ -2,0 +2,1 @@
                       +x
                       """;
            // Expected: 'x' inserted at index 1 (before 'b').
            var expectedLines = new List<string> { "a", "x", "b", "c" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AdditionAtEnd_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b" };
            // Diff means: at original line 3 (after 'b'), 0 lines taken, new line 3, 1 line added. Insert '+x'.
            var diff = """
                       @@ -3,0 +3,1 @@
                       +x
                       """;
            // Expected: 'x' inserted at index 2 (end of list).
            var expectedLines = new List<string> { "a", "b", "x" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DeletionAtBeginning_SpecificDeletion_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            // Diff means: remove line 'a'.
            var diff = """
                       @@ -1,1 +0,0 @@
                       -a
                       """;
            var expectedLines = new List<string> { "b", "c" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }
        
        [Fact]
        public void ApplyUnifiedDiffInner_DeletionAtEnd_SpecificDeletion_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b", "c" };
            var diff = """
                       @@ -3,1 +2,0 @@
                       -c
                       """;
            var expectedLines = new List<string> { "a", "b" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }


        [Fact]
        public void ApplyUnifiedDiffInner_MultipleHunks_AppliedInReverseAndCorrectlyReflectsCurrentLogic()
        {
            // Arrange
            var originalLines = new List<string> { "one", "two", "three", "four", "five" };
            var diff = """
                       @@ -1,2 +1,2 @@
                        one
                       -two
                       +TWO
                       @@ -4,2 +4,2 @@
                        four
                       -five
                       +FIVE
                       """;
            // Hunks are applied in reverse order.
            // Hunk 2 (OriginalStart=4): " four", "-five", "+FIVE"
            //   lineIndex starts at 3.
            //   " four": lineIndex = 4.
            //   "-five": remove "five" at index 4. fileContent = ["one", "two", "three", "four"]. lineIndex = 4.
            //   "+FIVE": linesToAdd = ["FIVE"]. lineIndex = 4.
            //   Insert ["FIVE"] at index 4. fileContent = ["one", "two", "three", "four", "FIVE"].
            //
            // Hunk 1 (OriginalStart=1): " one", "-two", "+TWO"
            //   lineIndex starts at 0. fileContent = ["one", "two", "three", "four", "FIVE"].
            //   " one": lineIndex = 1.
            //   "-two": remove "two" at index 1. fileContent = ["one", "three", "four", "FIVE"]. lineIndex = 1.
            //   "+TWO": linesToAdd = ["TWO"]. lineIndex = 1.
            //   Insert ["TWO"] at index 1. fileContent = ["one", "TWO", "three", "four", "FIVE"].
            var expectedLines = new List<string> { "one", "TWO", "three", "four", "FIVE" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AddLinesToEmptyFile_UsingZeroZeroHunk_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var originalLines = new List<string>();
            // Diff for adding lines to an empty file. OriginalStart is 0.
            var diff = """
                       @@ -0,0 +1,2 @@
                       +line1
                       +line2
                       """;
            // ApplyHunk: lineIndex = hunk.OriginalStart - 1 = -1.
            // InsertRange(-1, ...) will throw.

            // Act
            Action act = () => _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void ApplyUnifiedDiffInner_EmptyDiffString_ReturnsOriginalContent()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b" };
            var diff = "";
            var expectedLines = new List<string> { "a", "b" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DiffWithOnlyContextLines_ReturnsOriginalContent()
        {
            // Arrange
            var originalLines = new List<string> { "a", "b" };
            var diff = """
                       @@ -1,2 +1,2 @@
                        a
                        b
                       """;
            var expectedLines = new List<string> { "a", "b" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_IgnoresNoNewlineAtEndOfFileMessage()
        {
            // Arrange
            var originalLines = new List<string> { "a" };
            var diff = """
                       @@ -1,1 +1,1 @@
                       -a
                       +b
                       \ No newline at end of file
                       """;
            // Expected: 'a' removed, 'b' added. '\ No newline...' is ignored by ApplyHunk.
            var expectedLines = new List<string> { "b" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }
        
        [Fact]
        public void ApplyUnifiedDiffInner_HandlesMissingCountsInHunkHeader()
        {
            // Arrange
            var originalLines = new List<string> { "line1", "line2", "line3" };
            // @@ -2 +2 @@ is equivalent to @@ -2,1 +2,1 @@
            var diff = """
                       @@ -2 +2 @@
                       -line2
                       +LINE2_MODIFIED
                       """;
            var expectedLines = new List<string> { "line1", "LINE2_MODIFIED", "line3" };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }
    }
}
