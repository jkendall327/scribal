using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Scribal.AI;
using Scribal.Config;

namespace Scribal.Agency;

public sealed class GitCommitFilter(
    IGitServiceFactory factory,
    CommitGenerator generator,
    IOptions<AiSettings> aiSettings,
    ILogger<GitCommitFilter> logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext ctx,
        Func<FunctionInvocationContext, Task> next)
    {
        // Let the real function run first.
        await next(ctx);

        if (!factory.TryOpenRepository(out var git))
        {
            return;
        }

        if (aiSettings.Value.Weak is null)
        {
            return;
        }

        // Was it the edit-file tool?
        if (ctx.Function is {PluginName: nameof(DiffEditor), Name: DiffEditor.DiffEditorToolName})
        {
            logger.LogInformation("Creating commit after diff editor tool invocation");

            var file = ctx.Arguments["file"]?.ToString();
            var diff = ctx.Arguments["diff"]?.ToString();

            if (string.IsNullOrEmpty(file))
            {
                logger.LogWarning("No valid filepath could be provided to the Git service; exiting");

                return;
            }

            if (string.IsNullOrEmpty(diff))
            {
                logger.LogWarning("No valid diff returned from the tool call; exiting");

                return;
            }

            var message = await generator.GetCommitMessage(ctx.Kernel, [diff], aiSettings.Value.Weak.Provider);

            await git.CreateCommitAsync(file, message);
        }
    }
}