using Spectre.Console;

namespace Scribal.Cli.Interface;

public static class ProseDisplay
{
    public static void DisplayProsePassage(this IAnsiConsole console, string prose, string header)
    {
        var panel = new Panel(prose).Header($"[yellow]{header}[/]")
                                    .Border(BoxBorder.Rounded)
                                    .BorderColor(Color.Green);

        console.Write(panel);
    }
}