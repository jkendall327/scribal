using Scribal.AI;
using Spectre.Console;

namespace Scribal.Cli.Interface;

public static class ConsoleChatRenderer
{
    /// <summary>
    ///     Streams an LLM response to the console, showing a spinner
    ///     until the first token is available.
    /// </summary>
    public static async Task StreamWithSpinnerAsync(IAsyncEnumerable<ChatStreamItem> stream,
        CancellationToken ct = default)
    {
        // 1. Get an enumerator we can advance manually.
        await using var e = stream.GetAsyncEnumerator(ct);

        // 2. Show the spinner while we wait for MoveNextAsync to succeed.
        // If MoveNextAsync returns false the stream ended before we got any data.
        var gotFirst = await AnsiConsole.Status()
                                        .Spinner(Spinner.Known.Dots)
                                        .SpinnerStyle(Style.Parse("green"))
                                        .StartAsync("Thinking …", async _ => await e.MoveNextAsync(ct));

        // 3. The status panel is gone now.  If we received a first chunk, write it:
        if (!gotFirst)
        {
            AnsiConsole.MarkupLine("[red]The model produced no output.[/]");

            return;
        }

        // Write the first chunk immediately…
        ProcessChatStreamItem(e.Current);

        // …and continue to stream the rest.
        while (await e.MoveNextAsync(ct))
        {
            ProcessChatStreamItem(e.Current);
        }

        // Add a final newline so the prompt doesn’t start on the same line
        AnsiConsole.WriteLine();
    }

    /// <summary>
    ///     Displays a spinner while waiting for an async task to complete.
    ///     Does not write anything else to the console.
    /// </summary>
    public static async Task WaitWithSpinnerAsync(Task taskToAwait, CancellationToken ct = default)
    {
        await AnsiConsole.Status()
                         .Spinner(Spinner.Known.Dots)
                         .SpinnerStyle(Style.Parse("green"))
                         .StartAsync("Processing …", async _ => await taskToAwait.WaitAsync(ct));
    }

    private static void ProcessChatStreamItem(ChatStreamItem e)
    {
        switch (e)
        {
            case ChatStreamItem.TokenChunk tc: AnsiConsole.Write(tc.Content); break;
            case ChatStreamItem.Metadata md:
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();

                AnsiConsole.Decoration = Decoration.Italic;

                var time = FormatTimeSpan(md.Elapsed);

                AnsiConsole.Write($"{time}, {md.CompletionTokens} output tokens");

                AnsiConsole.ResetDecoration();

                break;
            }
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.TotalSeconds < 60
            ?

            // Less than a minute, just display seconds
            $"{timeSpan.Seconds}s"
            :

            // Display minutes and seconds
            $"{(int) timeSpan.TotalMinutes}m{timeSpan.Seconds}s";
    }
}