namespace Scribal.Cli;

internal sealed class CommandAutoCompletionHandler(CommandService svc) : IAutoCompleteHandler
{
    public char[] Separators { get; set; } = [' '];

    private readonly IReadOnlyList<string> _verbs = svc.GetCommandNames();

    public string[] GetSuggestions(string text, int pos) =>
        _verbs.Where(cmd => cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToArray();
}