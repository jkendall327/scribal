using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli;

public class InterfaceManager(
    CommandService commands,
    IFileSystem fileSystem,
    IAiChatService aiChatService,
    IGitService gitService,
    WorkspaceManager workspaceManager,
    IOptions<AiSettings> aiSettings,
    CancellationService cancellationService,
    RepoMapStore repoMapStore)
{
    private readonly Guid _conversationId = Guid.NewGuid();

    public Task DisplayWelcome()
    {
        AnsiConsole.Clear();

        var figlet = new FigletText("Scribal").LeftJustified().Color(Color.Green);

        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();
        
        if (!gitService.Enabled)
        {
            AnsiConsole.MarkupLine(
                "[red rapidblink]You are not in a valid Git repository! AI edits will be destructive![/]");
        }

        AnsiConsole.MarkupLine("Type [blue]--help[/] for available commands or just start typing to talk.");
        AnsiConsole.WriteLine();
        
        return Task.CompletedTask;
    }

    [DoesNotReturn]
    public async Task RunMainLoop()
    {
        var parser = commands.Build();
        
        ReadLine.HistoryEnabled = true;
        
        while (true)
        {
            AnsiConsole.Write(new Rule());

            if (repoMapStore.Paths.Any())
            {
                var filenames = repoMapStore.Paths
                    .Select(s => fileSystem.Path.GetFileName(s).ToLowerInvariant())
                    .ToList();
                
                var paths = string.Join(" | ", filenames);
                
                AnsiConsole.MarkupLine($"[yellow]{paths}[/]");
            }
            
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[green]> [/]");

            var input = ReadLine.Read();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }
            
            AnsiConsole.WriteLine();
            
            var parsed = parser.Parse(input);

            // If it fails to parse as a command, assume it's a message for the assistant.
            if (parsed.Errors.Any())
            {
                await ProcessConversation(input);
            }
            else
            {
                await parsed.InvokeAsync();
            }
        }
    }

    private async Task ProcessConversation(string userInput)
    {
        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();
        
        if (aiSettings.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No model is set. Check '.scribal/scribal.config'.[/]");
            return;
        }

        await DrawStatusLine();

        var files = repoMapStore.Paths.ToList();

        foreach (var file in files)
        {
            AnsiConsole.MarkupLine($"[yellow]{file}[/]");
        }

        try
        {
            var enumerable = aiChatService.StreamAsync(_conversationId.ToString(),
                userInput,
                aiSettings.Value.Primary.Provider,
                cancellationService.Source.Token);

            await StreamWithSpinnerAsync(enumerable, cancellationService.Source.Token);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("(cancelled)");
        }

        AnsiConsole.WriteLine();
    }

    private async Task DrawStatusLine()
    {
        var modelId = aiSettings.Value.Primary.ModelId;
        var workspace = workspaceManager.InWorkspace ? workspaceManager.TryFindWorkspaceFolder() : "not in workspace";
        var branch = gitService.Enabled ? await gitService.GetCurrentBranch() : "[not in a git repository]";
        
        AnsiConsole.MarkupLine($"[yellow]{modelId}[/] | {workspace} | {branch}");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Streams an LLM response to the console, showing a spinner
    /// until the first token is available.
    /// </summary>
    private static async Task StreamWithSpinnerAsync(IAsyncEnumerable<ChatStreamItem> stream,
        CancellationToken ct = default)
    {
        // 1. Get an enumerator we can advance manually.
        await using var e = stream.GetAsyncEnumerator(ct);

        // 2. Show the spinner while we wait for MoveNextAsync to succeed.
        // If MoveNextAsync returns false the stream ended before we got any data.
        var gotFirst = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("Thinking …", async _ => await e.MoveNextAsync());

        // 3. The status panel is gone now.  If we received a first chunk, write it:
        if (!gotFirst)
        {
            AnsiConsole.MarkupLine("[red]The model produced no output.[/]");
            return;
        }

        // Write the first chunk immediately…
        ProcessChatStreamItem(e.Current);

        // …and continue to stream the rest.
        while (await e.MoveNextAsync())
        {
            ProcessChatStreamItem(e.Current);
        }

        // Add a final newline so the prompt doesn’t start on the same line
        AnsiConsole.WriteLine();
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