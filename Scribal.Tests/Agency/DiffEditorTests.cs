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
            var mockFileSystem = Substitute.For<IFileSystem>();
            var mockOptions = Substitute.For<IOptions<AppConfig>>();
            mockOptions.Value.Returns(new AppConfig
            {
                DryRun = false
            });

            _sut = new DiffEditor(mockFileSystem, mockOptions);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleAddition_AddsLineCorrectlyInContext() // Name updated for clarity
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -1,3 +1,4 @@
                        a
                       +x
                        b
                        c
                       """;
            // Expected: 'x' is inserted between 'a' and 'b'
            var expectedLines = new List<string>
            {
                "a",
                "x",
                "b",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleDeletion_RemovesLineCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -1,3 +1,2 @@
                        a
                       -b
                        c
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_SimpleChange_AppliesChangeInPlace() // Name updated for clarity
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -1,3 +1,3 @@
                        a
                       -b
                       +x
                        c
                       """;
            // Expected: 'b' is replaced by 'x'
            var expectedLines = new List<string>
            {
                "a",
                "x",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_ChangeAtSpecificLine_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -2,1 +2,1 @@
                       -b
                       +x
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "x",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AdditionAtBeginning_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b"
            };
            var diff = """
                       @@ -1,0 +1,1 @@
                       +x
                       """;
            var expectedLines = new List<string>
            {
                "x",
                "a",
                "b"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AdditionInMiddle_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -2,0 +2,1 @@
                       +x
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "x",
                "b",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_AdditionAtEnd_UsingZeroOriginalCountHunk_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b"
            };
            var diff = """
                       @@ -3,0 +3,1 @@
                       +x
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "b",
                "x"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DeletionAtBeginning_SpecificDeletion_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -1,1 +0,0 @@
                       -a
                       """;
            var expectedLines = new List<string>
            {
                "b",
                "c"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DeletionAtEnd_SpecificDeletion_AppliesCorrectly()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -3,1 +2,0 @@
                       -c
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "b"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_MultipleHunks_AppliedInReverseAndCorrectly() // Name updated
        {
            // Arrange
            var originalLines = new List<string>
            {
                "one",
                "two",
                "three",
                "four",
                "five"
            };
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
            var expectedLines = new List<string>
            {
                "one",
                "TWO",
                "three",
                "four",
                "FIVE"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void
            ApplyUnifiedDiffInner_AddLinesToEmptyFile_UsingZeroZeroHunk_AppliesCorrectly() // Name and assertion updated
        {
            // Arrange
            var originalLines = new List<string>();
            var diff = """
                       @@ -0,0 +1,2 @@
                       +line1
                       +line2
                       """;
            var expectedLines = new List<string>
            {
                "line1",
                "line2"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_EmptyDiffString_ReturnsOriginalContent()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b"
            };
            var diff = "";
            var expectedLines = new List<string>
            {
                "a",
                "b"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DiffWithOnlyContextLines_ReturnsOriginalContent()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b"
            };
            var diff = """
                       @@ -1,2 +1,2 @@
                        a
                        b
                       """;
            var expectedLines = new List<string>
            {
                "a",
                "b"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_IgnoresNoNewlineAtEndOfFileMessageInDiffParsing()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a"
            };
            // The ParseUnifiedDiff method is now more strict about lines added to hunk.Lines.
            // It only adds lines starting with ' ', '+', or '-'.
            // So, "\ No newline at end of file" will not be in hunk.Lines.
            var diff = """
                       @@ -1,1 +1,1 @@
                       -a
                       +b
                       \ No newline at end of file 
                       """;
            var expectedLines = new List<string>
            {
                "b"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_HandlesMissingCountsInHunkHeader()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "line1",
                "line2",
                "line3"
            };
            var diff = """
                       @@ -2 +2 @@
                       -line2
                       +LINE2_MODIFIED
                       """;
            var expectedLines = new List<string>
            {
                "line1",
                "LINE2_MODIFIED",
                "line3"
            };

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            resultLines.Should().Equal(expectedLines);
        }

        [Fact]
        public void ApplyUnifiedDiffInner_ContextMismatch_ThrowsInvalidOperationException()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "different_b",
                "c"
            };
            var diff = """
                       @@ -1,3 +1,3 @@
                        a
                       -b
                       +x
                        c
                       """; // Diff expects 'b' but file has 'different_b'

            // Act
            Action act = () => _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage(
                    "Deletion mismatch at line 2. Expected to delete: 'b', Actual: 'different_b'. Hunk: @@ -1,3 +1,3 @@");
        }

        [Fact]
        public void ApplyUnifiedDiffInner_DeletionMismatch_ThrowsInvalidOperationException()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "a",
                "b",
                "c"
            };
            var diff = """
                       @@ -1,3 +1,2 @@
                        a
                       -unexpected_line_content 
                        c
                       """; // Diff expects to delete 'unexpected_line_content' at line 2

            // Act
            Action act = () => _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage(
                    "Deletion mismatch at line 2. Expected to delete: 'unexpected_line_content ', Actual: 'b'. Hunk: @@ -1,3 +1,2 @@");
        }

        [Fact]
        public void ApplyUnifiedDiffInner_ContextMismatchAtEndOfFile_ThrowsInvalidOperationException()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "line1"
            }; // File is shorter than diff expects
            var diff = """
                       @@ -1,2 +1,2 @@
                        line1
                        line2_expected_context
                       """;

            // Act
            Action act = () => _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage(
                    "Context mismatch at line 2. Expected: 'line2_expected_context', Actual: 'Out of bounds/End of file'. Hunk: @@ -1,2 +1,2 @@");
        }

        [Fact]
        public async Task ApplyUnifiedDiffInner_ComplexMultiHunkDiff_MatchesSnapshot()
        {
            // Arrange
            var originalLines = new List<string>
            {
                "Line 1: The quick brown fox",
                "Line 2: jumps over the lazy dog.",
                "Line 3: This is an important line.",
                "Line 4: Another line for context.",
                "Line 5: The middle section starts here.",
                "Line 6: A line to be modified.",
                "Line 7: A line to be deleted.",
                "Line 8: End of middle section.",
                "Line 9: Penultimate line.",
                "Line 10: The very last line."
            };
            var diff = """
                       @@ -1,4 +1,5 @@
                       -Line 1: The quick brown fox
                       +Line 1: The SLOW brown fox
                       +A new line inserted after original line 1.
                        Line 2: jumps over the lazy dog.
                        Line 3: This is an important line.
                        Line 4: Another line for context.
                       @@ -5,4 +6,3 @@
                        Line 5: The middle section starts here.
                       -Line 6: A line to be modified.
                       -Line 7: A line to be deleted.
                       +Line 6: This line was MODIFIED.
                        Line 8: End of middle section.
                       @@ -9,2 +9,3 @@
                        Line 9: Penultimate line.
                       -Line 10: The very last line.
                       +Line 10: The very final line.
                       +And one more line at the very end.
                       """;

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            await Verify(resultLines).UseDirectory("Snapshots");
        }

        [Fact]
        public async Task ApplyUnifiedDiffInner_CreateNewFileWithMultipleLines_MatchesSnapshot()
        {
            // Arrange
            var originalLines = new List<string>(); // Empty original file
            var diff = """
                       @@ -0,0 +1,7 @@
                       +Chapter 1: The Beginning
                       +
                       +It was a dark and stormy night.
                       +The wind howled through the trees.
                       +
                       +Suddenly, a shot rang out!
                       +--- End of File ---
                       """;

            // Act
            var resultLines = _sut.ApplyUnifiedDiffInner(originalLines, diff);

            // Assert
            await Verify(resultLines).UseDirectory("Snapshots");
        }
    }
}