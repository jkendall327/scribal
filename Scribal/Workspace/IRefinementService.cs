namespace Scribal.Cli.Infrastructure;

public interface IRefinementService
{
    Task<string> RefineAsync(string input, string systemPrompt, string sid, CancellationToken ct = default);
}