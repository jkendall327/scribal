namespace Scribal.Cli;

public sealed class CancellationService : IDisposable
{
    public CancellationTokenSource Source { get; private set; } = new();

    public void Initialise()
    {
        Console.CancelKeyPress += ConsoleOnCancelKeyPress;
    }

    private void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Keep app alive.
        e.Cancel = true;

        Source.Cancel();
        Source = new();
    }

    public void Dispose()
    {
        Source.Dispose();
        Console.CancelKeyPress -= ConsoleOnCancelKeyPress;
    }
}