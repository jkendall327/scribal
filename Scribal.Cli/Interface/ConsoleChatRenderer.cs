using Scribal.AI;
using Spectre.Console;

namespace Scribal.Cli.Interface;

public class ConsoleChatRenderer(IAnsiConsole console)
{
    /// <summary>
    ///     Streams an LLM response to the console, showing a spinner
    ///     until the first token is available.
    /// </summary>
    public async Task StreamWithSpinnerAsync(IAsyncEnumerable<ChatModels> stream, CancellationToken ct = default)
    {
        // 1. Get an enumerator we can advance manually.
        await using var e = stream.GetAsyncEnumerator(ct);

        // 2. Show the spinner while we wait for MoveNextAsync to succeed.
        // If MoveNextAsync returns false the stream ended before we got any data.
        var gotFirst = await console.Status()
                                    .Spinner(Spinner.Known.Dots)
                                    .SpinnerStyle(Style.Parse("green"))
                                    .StartAsync("Thinking …", async _ => await e.MoveNextAsync(ct));

        // 3. The status panel is gone now.  If we received a first chunk, write it:
        if (!gotFirst)
        {
            console.MarkupLine("[red]The model produced no output.[/]");

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
        console.WriteLine();
    }

    /// <summary>
    ///     Displays a spinner while waiting for an async task to complete.
    ///     Does not write anything else to the console.
    /// </summary>
    public async Task WaitWithSpinnerAsync(Task taskToAwait, CancellationToken ct = default)
    {
        await console.Status()
                     .Spinner(Spinner.Known.Dots)
                     .SpinnerStyle(Style.Parse("green"))
                     .StartAsync("Processing …", async _ => await taskToAwait.WaitAsync(ct));
    }

    private void ProcessChatStreamItem(ChatModels e)
    {
        switch (e)
        {
            case ChatModels.TokenChunk tc: console.Write(tc.Content); break;
            case ChatModels.Metadata md:
            {
                console.WriteLine();
                console.WriteLine();

                var time = FormatTimeSpan(md.Elapsed);

                console.Markup($"[italic]{time}, {md.CompletionTokens} output tokens[/]");

                break;
            }
        }
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
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