using Spectre.Console;

namespace Scribal.Cli;

public record Choices(string? Provider, string? Model, string? ApiKey, string? WeakModel);

public class ModelSelector
{
    private static string? _provider;
    private static string? _model;
    private static string? _weakModel;
    private static string? _apiKey;

    private static readonly Dictionary<string, List<string>> Providers = new()
    {
        {
            "OpenAI", [
                "gpt-4o",
                "gpt-4-turbo",
                "gpt-3.5-turbo"
            ]
        },
        {
            "Anthropic", [
                "claude-3-opus",
                "claude-3-sonnet",
                "claude-2.1"
            ]
        },
        {
            "Cohere", [
                "command-r+",
                "command-r",
                "command"
            ]
        }
    };

    public static Choices BeginConfiguration()
    {
        AnsiConsole.MarkupLine("[bold underline green]LLM CLI Configuration[/]\n");
        AnsiConsole.MarkupLine("Use the menu below to set your preferences.\n");

        while (true)
        {
            RenderStatusTable();

            var action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[bold]Select an option[/]")
                .AddChoices("Set Provider", "Set Model", "Set Weak Model (optional)", "Set API Key", "Exit"));

            switch (action)
            {
                case "Set Provider":
                    SetProvider();
                    break;
                case "Set Model":
                    SetModel();
                    break;
                case "Set Weak Model (optional)":
                    SetWeakModel();
                    break;
                case "Set API Key":
                    SetApiKey();
                    break;
                case "Exit":
                    return new(_provider, _model, _apiKey, _weakModel);
            }

            AnsiConsole.Clear();
        }
    }

    private static void RenderStatusTable()
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("Setting").LeftAligned());
        table.AddColumn(new TableColumn("Value").LeftAligned());

        table.AddRow("Provider", Colorize(_provider));
        table.AddRow("Model", Colorize(_model));
        table.AddRow("Weak Model", Colorize(_weakModel, allowEmpty: true));
        table.AddRow("API Key", string.IsNullOrEmpty(_apiKey) ? "[yellow]Not set[/]" : "[green]***********[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string Colorize(string? value, bool allowEmpty = false)
    {
        if (string.IsNullOrEmpty(value))
            return allowEmpty ? "[grey]None[/]" : "[yellow]Not set[/]";
        return $"[green]{value}[/]";
    }

    private static void SetProvider()
    {
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title("Choose a provider:").AddChoices(Providers.Keys));

        if (_provider != provider)
        {
            // Reset dependent fields when provider changes
            _model = null;
            _weakModel = null;
        }

        _provider = provider;
    }

    private static void SetModel()
    {
        if (_provider == null)
        {
            AnsiConsole.MarkupLine("[yellow]Please set a provider first.[/]");
            AnsiConsole.Console.Input.ReadKey(true);
            return;
        }

        var model = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title($"Choose a primary model for [green]{_provider}[/]:")
            .AddChoices(Providers[_provider]));

        _model = model;
    }

    private static void SetWeakModel()
    {
        if (_provider == null)
        {
            AnsiConsole.MarkupLine("[yellow]Please set a provider first.[/]");
            AnsiConsole.Console.Input.ReadKey(true);
            return;
        }

        var choices = new List<string>(Providers[_provider])
        {
            "<None>"
        };

        var weakModel = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title($"Choose a weak model for [green]{_provider}[/] (or <None>):")
            .AddChoices(choices));

        _weakModel = weakModel == "<None>" ? null : weakModel;
    }

    private static void SetApiKey()
    {
        var key = AnsiConsole.Prompt(new TextPrompt<string>("Enter API key:").PromptStyle("green").Secret());

        _apiKey = key;
    }
}