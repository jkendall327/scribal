namespace Scribal.Cli;

using Microsoft.SemanticKernel;
using LibGit2Sharp;                                 // or any git wrapper

public sealed class GitCommitFilter : IFunctionInvocationFilter
{
    private readonly Repository _repo;

    public GitCommitFilter(Repository repo) => _repo = repo;

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext ctx,
        Func<FunctionInvocationContext, Task> next)
    {
        // Let the real function run first.
        await next(ctx);

        // Was it the edit-file tool?
        if (ctx.Function is {PluginName: nameof(DiffEditor), Name: nameof(DiffEditor.ApplyUnifiedDiffAsync)})
        {
            // contents of the "path" argument make a nice commit message
            var file = ctx.Arguments["path"]?.ToString() ?? "<unknown>";

            Commands.Stage(_repo, file);
            var sig = new Signature("AI-assistant", "ai@example.com", DateTimeOffset.Now);
            _repo.Commit($"AI edited {file}", sig, sig);

            Console.WriteLine($"[git] committed {file}");
        }
    }
}
