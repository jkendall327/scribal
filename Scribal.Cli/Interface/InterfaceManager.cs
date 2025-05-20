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
    IUserInteraction userInteraction) // Replaced ConsoleChatRenderer
{
    private readonly IUserInteraction _userInteraction = userInteraction;
    private readonly Guid _conversationId = Guid.NewGuid();
    private CancellationTokenSource _cts = new();

    public async Task DisplayWelcome()
    {
        await _userInteraction.ClearAsync();

        var figlet = new FigletText("Scribal").LeftJustified().Color(Color.Green);
        await _userInteraction.WriteFigletAsync(figlet);
        await _userInteraction.NotifyAsync(""); // For spacing

        if (!gitFactory.TryOpenRepository(out var _))
        {
            await _userInteraction.NotifyAsync(
                "[red rapidblink]You are not in a valid Git repository! AI edits will be destructive![/]", new(MessageType.Error));
        }

        await _userInteraction.NotifyAsync("Type [blue]/help[/] for available commands or just start typing to talk.");
        await _userInteraction.NotifyAsync("[yellow](hint: use '{' to enter multi-line mode)[/]"); // Simplified hint
        await _userInteraction.NotifyAsync(""); // For spacing
    }

    [DoesNotReturn]
    public async Task RunMainLoop()
    {
        var parser = commands.Build();
        // ReadLine.HistoryEnabled = true; // IUserInteraction.GetUserInputAsync may or may not support history. Assuming it does or it's managed elsewhere.

        while (true)
        {
            await _userInteraction.WriteRuleAsync(new Rule());
            await DrawStatusLine(aiSettings.Value.Primary?.ModelId);
            await _userInteraction.NotifyAsync(""); // For spacing

            // IUserInteraction.GetUserInputAsync does not display a prompt prefix.
            // A general prompt message is preferred before calling GetUserInputAsync.
            await _userInteraction.NotifyAsync("Enter input ([green]>[/]):"); 
            var lineInput = await _userInteraction.GetUserInputAsync(_cts.Token);

            if (lineInput == "{")
            {
                // Prompt for multi-line input is part of GetMultilineInputAsync
                var fullInput = await _userInteraction.GetMultilineInputAsync(
                    "Entering multi-line input mode. End with a blank line then Ctrl+D/Ctrl+Z, or type 'eof' on a new line.");
                
                await _userInteraction.NotifyAsync(""); // Spacing after multi-line input

                if (!string.IsNullOrWhiteSpace(fullInput))
                {
                    await ActOnInput(fullInput, parser);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(lineInput))
                {
                    continue;
                }
                await _userInteraction.NotifyAsync(""); // Spacing after single-line input
                await ActOnInput(lineInput, parser);
            }
        }
    }

    private async Task ActOnInput(string input, Parser parser)
    {
        var command = input.StartsWith('/');

        if (!command)
        {
            await ProcessConversation(input);
            return;
        }

        if (input is "/help")
        {
            input = "--help"; // System.CommandLine convention
        }

        var parsed = parser.Parse(input);

        if (parsed.Errors.Any())
        {
            await _userInteraction.NotifyAsync("There was an issue parsing that command, sorry.", new(MessageType.Error));
            await _userInteraction.NotifyAsync("(hint: some commands require their arguments to be quoted)", new(MessageType.Hint));
            await _userInteraction.NotifyAsync("(hint: for example, '/outline \"a sci-fi epic about a horse\"')", new(MessageType.Hint));
        }
        else
        {
            // This will internally call methods that now use _userInteraction
            await parsed.InvokeAsync(); 
        }
    }

    private async Task DrawStatusLine(string? modelId)
    {
        var workspace = workspaceManager.InWorkspace
            ? WorkspaceManager.TryFindWorkspaceFolder(fileSystem) ?? "N/A" 
            : "not in workspace";

        var branch = gitFactory.TryOpenRepository(out var git) ? await git.GetCurrentBranch() : "not in a git repository";
        var model = modelId is null ? "[yellow]no model![/]" : $"[yellow]{modelId}[/]";

        await _userInteraction.NotifyAsync($"{model} | {workspace} | {branch}");
        await DrawPathsIfAny();
    }

    private async Task DrawPathsIfAny()
    {
        if (!repoMapStore.Paths.Any())
        {
            return;
        }
        await _userInteraction.NotifyAsync(""); // For spacing
        var filenames = repoMapStore.Paths.Select(s => fileSystem.Path.GetFileName(s).ToLowerInvariant()).ToList();
        var paths = string.Join(" | ", filenames);
        await _userInteraction.NotifyAsync($"[yellow]{paths}[/]");
    }

    private async Task ProcessConversation(string userInput)
    {
        Console.CancelKeyPress += PerformCancellation;

        await _userInteraction.WriteRuleAsync(new Rule());
        await _userInteraction.NotifyAsync(""); // For spacing

        if (aiSettings.Value.Primary is null)
        {
            await _userInteraction.NotifyAsync("No model is set. Check '.scribal/scribal.config'.", new(MessageType.Error));
            return;
        }

        var files = repoMapStore.Paths.ToList();
        foreach (var file in files)
        {
            // Displaying files being used in context
            await _userInteraction.NotifyAsync($"Using context file: [yellow]{file}[/]", new(MessageType.Hint));
        }

        try
        {
            var sid = aiSettings.Value.Primary.Provider;
            var chatRequest = new ChatRequest(userInput, _conversationId.ToString(), sid);
            var enumerable = aiChatService.StreamAsync(chatRequest, null, _cts.Token);
            
            // Use IUserInteraction to display the streaming response
            await _userInteraction.DisplayAssistantResponseAsync(enumerable, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            await _userInteraction.NotifyAsync(""); // For spacing
            await _userInteraction.NotifyAsync("(cancelled)", new(MessageType.Warning));
        }

        await _userInteraction.NotifyAsync(""); // For spacing

        Console.CancelKeyPress -= PerformCancellation;
        _cts = new CancellationTokenSource(); // Reset CTS for next operation
    }

    private void PerformCancellation(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts.Cancel();
    }
}