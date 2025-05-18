using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text;
using Microsoft.Extensions.Options;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Cli.Features;
using Scribal.Config;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Interface;

public class InterfaceManager(
    CommandService commands,
    IFileSystem fileSystem,
    IAiChatService aiChatService,
    IGitServiceFactory gitFactory,
    WorkspaceManager workspaceManager,
    IOptions<AiSettings> aiSettings,
    RepoMapStore repoMapStore,
    ConsoleChatRenderer consoleRenderer)
{
    private readonly Guid _conversationId = Guid.NewGuid();
    private CancellationTokenSource _cts = new();

    public Task DisplayWelcome()
    {
        AnsiConsole.Clear();

        var figlet = new FigletText("Scribal").LeftJustified().Color(Color.Green);

        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();

        if (!gitFactory.TryOpenRepository(out var _))
        {
            AnsiConsole.MarkupLine(
                "[red rapidblink]You are not in a valid Git repository! AI edits will be destructive![/]");
        }

        AnsiConsole.MarkupLine("Type [blue]/help[/] for available commands or just start typing to talk.");
        AnsiConsole.MarkupLine("[yellow](hint: use '{' to enter multi-line mode and '}' to exit)[/]");
        AnsiConsole.WriteLine();

        return Task.CompletedTask;
    }

    [DoesNotReturn]
    public async Task RunMainLoop()
    {
        var parser = commands.Build();

        ReadLine.HistoryEnabled = true;
        var inMultiLineMode = false;
        var multiLineInputBuilder = new StringBuilder();

        while (true)
        {
            AnsiConsole.Write(new Rule());

            await DrawStatusLine(aiSettings.Value.Primary?.ModelId);

            AnsiConsole.WriteLine();

            // AI: Use a different prompt when in multi-line mode for better UX
            AnsiConsole.Markup(inMultiLineMode ? "[blue]... [/]" : "[green]> [/]");

            var lineInput = ReadLine.Read();

            if (inMultiLineMode)
            {
                if (lineInput == "}")
                {
                    inMultiLineMode = false;
                    var fullInput = multiLineInputBuilder.ToString();
                    multiLineInputBuilder.Clear();

                    AnsiConsole.WriteLine();

                    if (!string.IsNullOrWhiteSpace(fullInput))
                    {
                        await ActOnInput(fullInput, parser);
                    }
                }
                else
                {
                    multiLineInputBuilder.AppendLine(lineInput);
                }
            }
            else
            {
                if (lineInput == "{")
                {
                    inMultiLineMode = true;
                    multiLineInputBuilder.Clear();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(lineInput))
                    {
                        continue;
                    }

                    AnsiConsole.WriteLine();
                    await ActOnInput(lineInput, parser);
                }
            }
        }
    }

    private async Task ActOnInput(string input, Parser parser)
    {
        // Normal chat/agentic request.
        var command = input.StartsWith('/');

        if (!command)
        {
            await ProcessConversation(input);

            return;
        }

        if (input is "/help")
        {
            // Working around System.CommandLine seeming to hardcode this.
            input = "--help";
        }

        var parsed = parser.Parse(input);

        if (parsed.Errors.Any())
        {
            AnsiConsole.MarkupLine("[red]There was an issue parsing that command, sorry.[/]");
            AnsiConsole.MarkupLine("[yellow](hint: some commands require their arguments to be quoted)[/]");
            AnsiConsole.MarkupLine("[yellow](hint: for example, '/outline \"a sci-fi epic about a horse\"')[/]");
        }
        else
        {
            await parsed.InvokeAsync();
        }
    }

    private async Task DrawStatusLine(string? modelId)
    {
        var workspace = workspaceManager.InWorkspace
            ? WorkspaceManager.TryFindWorkspaceFolder(fileSystem)
            : "not in workspace";

        var branch = gitFactory.TryOpenRepository(out var git) ? await git.GetCurrentBranch() : "not in a git repository";

        var model = modelId is null ? "[yellow]no model![/]" : $"[yellow]{modelId}[/]";

        AnsiConsole.MarkupLine($"{model} | {workspace} | {branch}");

        DrawPathsIfAny();
    }

    private void DrawPathsIfAny()
    {
        if (!repoMapStore.Paths.Any())
        {
            return;
        }

        AnsiConsole.WriteLine();

        var filenames = repoMapStore.Paths.Select(s => fileSystem.Path.GetFileName(s).ToLowerInvariant()).ToList();

        var paths = string.Join(" | ", filenames);

        AnsiConsole.MarkupLine($"[yellow]{paths}[/]");
    }

    private async Task ProcessConversation(string userInput)
    {
        Console.CancelKeyPress += PerformCancellation;

        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();

        if (aiSettings.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No model is set. Check '.scribal/scribal.config'.[/]");

            return;
        }

        var files = repoMapStore.Paths.ToList();

        foreach (var file in files)
        {
            AnsiConsole.MarkupLine($"[yellow]{file}[/]");
        }

        try
        {
            var sid = aiSettings.Value.Primary.Provider;

            var chatRequest = new ChatRequest(userInput, _conversationId.ToString(), sid);

            var enumerable = aiChatService.StreamAsync(chatRequest, null, _cts.Token);
            await consoleRenderer.StreamWithSpinnerAsync(enumerable, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("(cancelled)");
        }

        AnsiConsole.WriteLine();

        Console.CancelKeyPress -= PerformCancellation;
        _cts = new();
    }

    private void PerformCancellation(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts.Cancel();
    }
}