using Scribal.Context;
using Spectre.Console;

namespace Scribal.Cli;

public class StickyTreeSelector
{
    private readonly FileSystemNode _root;
    private List<FileSystemNode> _visibleNodes;
    private int _currentNodeIndex;

    private const string ExpandedPrefix = "v ";
    private const string CollapsedPrefix = "> ";
    private const string FileIcon = "  ";

    public StickyTreeSelector(string rootPath)
    {
        _root = BuildFileSystemTree(rootPath);
        _root.IsExpanded = true;
        _visibleNodes = GetVisibleNodes(_root);
        _currentNodeIndex = 0;
    }

    public static List<string> Scan(string startPath)
    {
        AnsiConsole.MarkupLine($"Scanning starting from: [cyan]{startPath}[/]");

        try
        {
            var selector = new StickyTreeSelector(startPath);
            var selectedItems = selector.GetSelectedPaths();

            if (selectedItems.Any())
            {
                AnsiConsole.MarkupLine("\n[green]You selected:[/]");

                foreach (var item in selectedItems)
                {
                    AnsiConsole.MarkupLine($"- [blue]{item}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("\n[yellow]No items were selected.[/]");
            }

            return selectedItems;
        }
        catch (DirectoryNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");

            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error: Insufficient permissions to access parts of the directory tree. {ex.Message}[/]");

            return [];
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);

            return [];
        }
    }

    public List<string> GetSelectedPaths()
    {
        Console.Clear();

        AnsiConsole.Live(new Markup(Markup.Escape(_root.Name)))
                   .Start(ctx =>
                   {
                       while (true)
                       {
                           _visibleNodes = GetVisibleNodes(_root);

                           if (_currentNodeIndex >= _visibleNodes.Count)
                           {
                               _currentNodeIndex = Math.Max(0, _visibleNodes.Count - 1);
                           }

                           var treeWidget = BuildSpectreTreeWidget();
                           ctx.UpdateTarget(treeWidget);
                           ctx.Refresh();

                           var keyInfo = Console.ReadKey(true);

                           if (keyInfo.Key == ConsoleKey.Backspace)
                           {
                               break;
                           }

                           _ = HandleKeyPress(keyInfo);
                       }
                   });

        var selectedPaths = CollectSelectedPaths(_root);
        Console.Clear();
        AnsiConsole.MarkupLine("[green]Selection complete.[/]");

        return selectedPaths;
    }

    private bool HandleKeyPress(ConsoleKeyInfo keyInfo)
    {
        if (_visibleNodes.Count == 0)
        {
            return false;
        }

        var currentNode = _visibleNodes[_currentNodeIndex];
        var needsRedraw = false;

        switch (keyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                _currentNodeIndex = Math.Max(0, _currentNodeIndex - 1);
                needsRedraw = true;

                break;

            case ConsoleKey.DownArrow:
                _currentNodeIndex = Math.Min(_visibleNodes.Count - 1, _currentNodeIndex + 1);
                needsRedraw = true;

                break;

            case ConsoleKey.Enter:
                // Determine the NEW state we want to apply
                var newSelectedState = !currentNode.IsSelected;

                SetSelectionRecursive(currentNode, newSelectedState);

                needsRedraw = true;

                break;

            case ConsoleKey.RightArrow:
            {
                currentNode.IsExpanded = true;
                needsRedraw = true;
            }

                break;

            case ConsoleKey.LeftArrow:
            {
                currentNode.IsExpanded = false;
                needsRedraw = true;
            }

                break;
        }

        return needsRedraw;
    }

    private void SetSelectionRecursive(FileSystemNode node, bool isSelected)
    {
        node.IsSelected = isSelected;

        foreach (var child in node.Children)
        {
            SetSelectionRecursive(child, isSelected);
        }
    }

    private FileSystemNode BuildFileSystemTree(string path)
    {
        var dirInfo = new DirectoryInfo(path);

        if (!dirInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var rootNode = new FileSystemNode(dirInfo.FullName, true);
        BuildTreeRecursive(rootNode, dirInfo);

        return rootNode;
    }

    private void BuildTreeRecursive(FileSystemNode parentNode, DirectoryInfo currentDir)
    {
        try
        {
            foreach (var dir in currentDir.GetDirectories())
            {
                var childDirNode = new FileSystemNode(dir.FullName, true);
                parentNode.AddChild(childDirNode);
                BuildTreeRecursive(childDirNode, dir);
            }

            foreach (var file in currentDir.GetFiles("*.md"))
            {
                var childFileNode = new FileSystemNode(file.FullName, false);
                parentNode.AddChild(childFileNode);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error accessing {currentDir.FullName}: {ex.Message}[/]");
        }
    }

    private List<FileSystemNode> GetVisibleNodes(FileSystemNode node)
    {
        var list = new List<FileSystemNode>();
        AddVisibleNodesRecursive(node, list);

        return list;
    }

    private void AddVisibleNodesRecursive(FileSystemNode node, List<FileSystemNode> list)
    {
        list.Add(node);

        if (!node.IsDirectory || !node.IsExpanded)
        {
            return;
        }

        var sortedChildren = node.Children.OrderBy(c => !c.IsDirectory).ThenBy(c => c.Name);

        foreach (var child in sortedChildren)
        {
            AddVisibleNodesRecursive(child, list);
        }
    }

    private Tree BuildSpectreTreeWidget()
    {
        var tree = new Tree("[yellow underline]Select Files/Folders[/]")
        {
            Guide = TreeGuide.Line
        };

        var globalNodeIndex = 0;
        AddNodeToSpectreTree(_root, tree, ref globalNodeIndex);

        return tree;
    }

    private void AddNodeToSpectreTree(FileSystemNode node, IHasTreeNodes parentSpectreNode, ref int globalNodeIndex)
    {
        var isCurrentNode = _visibleNodes.Count > _currentNodeIndex && _visibleNodes[_currentNodeIndex] == node;

        const string selectedMarker = "*";
        const string unselectedMarker = " ";
        var icon = node.IsDirectory ? node.IsExpanded ? ExpandedPrefix : CollapsedPrefix : FileIcon;

        var name = Markup.Escape(node.Name);

        var baseLabelText = $"{(node.IsSelected ? selectedMarker : unselectedMarker)} {icon}{name}";

        string finalLabelMarkup;

        if (isCurrentNode)
        {
            var styledBase = node.IsSelected ? $"[bold]{baseLabelText}[/]" : baseLabelText;
            finalLabelMarkup = $"[underline yellow on blue]{styledBase}[/]";
        }
        else if (node.IsSelected)
        {
            finalLabelMarkup = $"[bold]{baseLabelText}[/]";
        }
        else
        {
            finalLabelMarkup = $"[dim]{baseLabelText}[/]";
        }

        var treeNode = parentSpectreNode.AddNode(finalLabelMarkup);

        var isNodeVisibleInList = globalNodeIndex < _visibleNodes.Count && _visibleNodes[globalNodeIndex] == node;
        globalNodeIndex++;

        if (!node.IsDirectory || !node.IsExpanded || !node.Children.Any() || !isNodeVisibleInList)
        {
            return;
        }

        var sortedChildren = node.Children.OrderBy(c => !c.IsDirectory).ThenBy(c => c.Name);

        foreach (var child in sortedChildren)
        {
            AddNodeToSpectreTree(child, treeNode, ref globalNodeIndex);
        }
    }

    private List<string> CollectSelectedPaths(FileSystemNode node)
    {
        var selected = new List<string>();
        CollectSelectedRecursive(node, selected);

        return selected;
    }

    private void CollectSelectedRecursive(FileSystemNode node, List<string> selectedList)
    {
        if (node.IsSelected)
        {
            selectedList.Add(node.Path);
        }

        foreach (var child in node.Children)
        {
            CollectSelectedRecursive(child, selectedList);
        }
    }
}