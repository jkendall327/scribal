using System.Text;
using Spectre.Console;

namespace Scribal.Cli.Interface;

public static class ExceptionDisplay
{
    /// <summary>
    ///     AOT-friendly mechanism of writing out exceptions.
    /// </summary>
    /// <param name="ex">the exception to display.</param>
    /// <param name="console">The console to write to.</param>
    public static void DisplayException(Exception ex, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;

        console.MarkupLine("[bold red]An error occurred:[/]");

        var panel = new Panel(GetExceptionMarkup(ex)).Header("[yellow]Exception Details[/]")
                                                     .Border(BoxBorder.Rounded)
                                                     .BorderColor(Color.Red);

        console.Write(panel);
    }

    private static Markup GetExceptionMarkup(Exception ex)
    {
        var sb = new StringBuilder();
        var currentException = ex;
        var exceptionCount = 0;

        while (currentException != null)
        {
            if (exceptionCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[grey]---> Inner Exception:[/]");
            }

            var fullName = currentException.GetType().FullName;

            if (fullName == null)
            {
                throw new InvalidOperationException("Exception somehow did not have a sensible type name.");
            }

            sb.Append($"[bold aqua]{Markup.Escape(fullName)}[/]: ");
            sb.AppendLine($"[white]{Markup.Escape(currentException.Message)}[/]");

            if (!string.IsNullOrWhiteSpace(currentException.StackTrace))
            {
                sb.AppendLine("[dim]Stack Trace:[/]");

                // Escape the stack trace to prevent any accidental markup interpretation
                // And indent it slightly for readability
                var enumerable = currentException.StackTrace.Split([
                        Environment.NewLine
                    ],
                    StringSplitOptions.None);

                foreach (var line in enumerable)
                {
                    sb.AppendLine($"  [grey]{Markup.Escape(line.Trim())}[/]");
                }
            }

            currentException = currentException.InnerException;
            exceptionCount++;
        }

        return new(sb.ToString());
    }
}