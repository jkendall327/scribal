using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli.Interface;
using Spectre.Console;

namespace Scribal.Cli.Infrastructure;

public class RefinementService(
    IAnsiConsole console,
    ConsoleChatRenderer consoleChatRenderer,
    IAiChatService chat,
    ILogger<RefinementService> logger)
{
    public async Task<string> RefineAsync(string input, string systemPrompt, string sid, CancellationToken ct = default)
    {
        var refinementCid = $"{Guid.NewGuid()}";

        var refinementHistory = new ChatHistory();

        refinementHistory.AddSystemMessage(systemPrompt);
        refinementHistory.AddAssistantMessage(input);

        var lastAssistantResponse = input;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                console.MarkupLine("[yellow]Refinement cancelled.[/]");
                logger.LogInformation("Refinement cancelled by host for {RefinementCid}", refinementCid);

                break;
            }

            console.WriteLine();

            console.MarkupLine("(available commands: [blue]/done[/], [blue]/cancel[/])");

            console.Markup("[green]Refine > [/]");
            var userInput = ReadLine.Read();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals("/done", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("User finished draft refinement for {RefinementCid}", refinementCid);

                return lastAssistantResponse;
            }

            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                console.MarkupLine("[yellow]Cancelling refinement and using original output...[/]");
                logger.LogInformation("User cancelled draft refinement for {RefinementCid}", refinementCid);

                return input;
            }

            console.WriteLine();
            var responseBuilder = new StringBuilder();

            try
            {
                refinementHistory.AddUserMessage(userInput);

                var chatRequest = new ChatRequest(userInput, refinementCid, sid);

                var refinementStream = chat.StreamAsync(chatRequest, refinementHistory, ct);

                await consoleChatRenderer.StreamWithSpinnerAsync(
                    refinementStream.CollectWhileStreaming(responseBuilder, ct),
                    ct);

                lastAssistantResponse = responseBuilder.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(lastAssistantResponse))
                {
                    refinementHistory.AddAssistantMessage(lastAssistantResponse);

                    logger.LogDebug("Refinement successful for {RefinementCid}, response length {Length}",
                        refinementCid,
                        lastAssistantResponse.Length);
                }
            }
            catch (OperationCanceledException)
            {
                console.WriteLine();
                console.MarkupLine("[yellow](Refinement stream cancelled)[/]");
                logger.LogInformation("Refinement stream cancelled for {RefinementCid}", refinementCid);

                break;
            }
            catch (Exception e)
            {
                ExceptionDisplay.DisplayException(e, console);
                console.MarkupLine("[red]An error occurred during refinement.[/]");
                logger.LogError(e, "Error during draft refinement for {RefinementCid}", refinementCid);
            }
        }

        return lastAssistantResponse;
    }
}