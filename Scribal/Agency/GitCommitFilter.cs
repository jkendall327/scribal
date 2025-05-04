using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Scribal.Cli;

namespace Scribal.Agency;

public sealed class GitCommitFilter(IGitService git, ILogger<GitCommitFilter> logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext ctx,
        Func<FunctionInvocationContext, Task> next)
    {
        // Let the real function run first.
        await next(ctx);

        if (!git.Enabled)
        {
            return;
        }

        // Was it the edit-file tool?
        if (ctx.Function is {PluginName: nameof(DiffEditor), Name: nameof(DiffEditor.ApplyUnifiedDiffAsync)})
        {
            logger.LogInformation("Creating commit after diff editor tool invocation");
            
            var file = ctx.Arguments["path"]?.ToString();

            if (string.IsNullOrEmpty(file))
            {
                logger.LogWarning("No valid filepath could be provided to the Git service; exiting");
                return;
            }
            
            // TODO: generate an actual message here.
            await git.CreateCommit(file, "test message");
        }
    }
}