using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
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
    IGitService gitService,
    WorkspaceManager workspaceManager,
    IOptions<AiSettings> aiSettings,
    RepoMapStore repoMapStore)
{
    private readonly Guid _conversationId = Guid.NewGuid();
    private CancellationTokenSource _cts = new();

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

            await DrawStatusLine(aiSettings.Value.Primary?.ModelId);

            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[green]> [/]");

            var input = ReadLine.Read();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            AnsiConsole.WriteLine();

            await ActOnInput(input, parser);
        }
    }

    private async Task ActOnInput(string input, Parser parser)
    {
        // Normal chat/agentic request.
        if (!input.StartsWith('/'))
        {
            await ProcessConversation(input);

            return;
        }

        var parsed = parser.Parse(input);

        if (parsed.Errors.Any())
        {
            AnsiConsole.MarkupLine("[red]There was an issue parsing that command, sorry.[/]");
            AnsiConsole.MarkupLine("[yellow](hint: most commands require their arguments to be quoted)[/]");
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

        var branch = gitService.Enabled ? await gitService.GetCurrentBranch() : "[not in a git repository]";

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
            var enumerable = aiChatService.StreamAsync(_conversationId.ToString(),
                userInput,
                aiSettings.Value.Primary.Provider,
                _cts.Token);

            await ConsoleChatRenderer.StreamWithSpinnerAsync(enumerable, _cts.Token);
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