using Spectre.Console;

namespace Scribal.Cli;

public sealed class StatusBar : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public StatusBar(Func<Task<string>> renderer)
    {
        _ = Task.Run(async () =>
        {
            var panel = new Panel(await renderer()).NoBorder().Expand();

            var live = AnsiConsole.Live(panel).AutoClear(false).Overflow(VerticalOverflow.Crop);

            await live.StartAsync(async ctx =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    panel = new Panel(await renderer()).NoBorder().Expand();
                    //ctx.Refresh();
                    //ctx.UpdateTarget(panel);
                    await Task.Delay(500);
                }
            });
        });
    }

    public void Dispose() => _cts.Cancel();
}