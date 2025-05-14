using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Scribal.Context;

public class MarkdownMapExtractor
{
    public static List<HeaderInfo> ExtractHeaders(string markdownText)
    {
        // Parse the markdown document
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var document = Markdig.Parsers.MarkdownParser.Parse(markdownText, pipeline);

        var headers = new List<HeaderInfo>();

        // Traverse the document and find all heading blocks
        foreach (var block in document.Descendants())
        {
            if (block is not HeadingBlock headingBlock) continue;

            var headerText = ExtractTextFromHeading(headingBlock);

            headers.Add(new()
            {
                Level = headingBlock.Level,
                Text = headerText,
                Line = headingBlock.Line
            });
        }

        return headers;
    }

    private static string ExtractTextFromHeading(HeadingBlock headingBlock)
    {
        // Extract the text content from the heading
        var text = "";

        if (headingBlock.Inline is null)
        {
            return text.Trim();
        }

        foreach (var inline in headingBlock.Inline)
        {
            if (inline is LiteralInline literalInline)
            {
                text += literalInline.Content.ToString();
            }
        }

        return text.Trim();
    }
}

// Simple class to store header information
public class HeaderInfo
{
    public int Level { get; set; } // H1, H2, etc. (1, 2, etc.)
    public required string Text { get; set; } // The header text
    public int Line { get; set; } // Line number in the document

    public override string ToString()
    {
        return $"{new string('#', Level)} {Text} (Line {Line})";
    }
}
