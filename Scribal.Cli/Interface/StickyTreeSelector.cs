using Scribal.Context;
using Spectre.Console;

using Scribal.Cli.Features; // For MessageType, MessageOptions if needed (though IUserInteraction should handle this)

namespace Scribal.Cli.Interface;

public class StickyTreeSelector
{
    private readonly IUserInteraction _userInteraction;
    private FileSystemNode _root = null!; // Initialized in InitializeAsync
    private List<FileSystemNode> _visibleNodes = null!; // Initialized in InitializeAsync
    private int _currentNodeIndex;

    private const string ExpandedPrefix = "v ";
    private const string CollapsedPrefix = "> ";
    private const string FileIcon = "  ";

    public StickyTreeSelector(IUserInteraction userInteraction)
    {
        _userInteraction = userInteraction;
    }

    private async Task InitializeAsync(string rootPath)
    {
        _root = await BuildFileSystemTreeAsync(rootPath);
        _root.IsExpanded = true; // Expand the root node by default
        _visibleNodes = GetVisibleNodes(_root);
        _currentNodeIndex = 0;
    }

    public async Task<List<string>> ScanAsync(string startPath, CancellationToken cancellationToken = default)
    {
        await _userInteraction.NotifyAsync($"Scanning starting from: {startPath}", cancellationToken: cancellationToken);

        try
        {
            await InitializeAsync(startPath); // Initialize the tree structure
            var selectedItems = GetSelectedPaths(); // This remains synchronous as per plan for core logic

            if (selectedItems.Any())
            {
                await _userInteraction.NotifyAsync("\nYou selected:", new(MessageType.Informational), cancellationToken);
                foreach (var item in selectedItems)
                {
                    await _userInteraction.NotifyAsync($"- {item}", new(MessageType.Informational), cancellationToken);
                }
            }
            else
            {
                await _userInteraction.NotifyAsync("\nNo items were selected.", new(MessageType.Warning), cancellationToken);
            }

            return selectedItems;
        }
        catch (DirectoryNotFoundException ex)
        {
            await _userInteraction.NotifyAsync($"Error: {ex.Message}", new(MessageType.Error), cancellationToken);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            await _userInteraction.NotifyAsync(
                $"Error: Insufficient permissions to access parts of the directory tree. {ex.Message}", new(MessageType.Error), cancellationToken);
            return [];
        }
        catch (Exception ex)
        {
            await _userInteraction.NotifyError("An error occurred during file selection.", ex, cancellationToken);
            return [];
        }
    }

    // Core interactive logic remains synchronous as per instructions
    private List<string> GetSelectedPaths()
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

    private async Task<FileSystemNode> BuildFileSystemTreeAsync(string path, CancellationToken cancellationToken = default)
    {
        var dirInfo = new DirectoryInfo(path);
        if (!dirInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var rootNode = new FileSystemNode(dirInfo.FullName, true);
        await BuildTreeRecursiveAsync(rootNode, dirInfo, cancellationToken);
        return rootNode;
    }

    private async Task BuildTreeRecursiveAsync(FileSystemNode parentNode, DirectoryInfo currentDir, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var dir in currentDir.GetDirectories())
            {
                if (cancellationToken.IsCancellationRequested) return;
                var childDirNode = new FileSystemNode(dir.FullName, true);
                parentNode.AddChild(childDirNode);
                await BuildTreeRecursiveAsync(childDirNode, dir, cancellationToken);
            }

            foreach (var file in currentDir.GetFiles("*.md"))
            {
                if (cancellationToken.IsCancellationRequested) return;
                var childFileNode = new FileSystemNode(file.FullName, false);
                parentNode.AddChild(childFileNode);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Optionally notify about skipped directories due to permissions
            // await _userInteraction.NotifyAsync($"Skipping directory due to permissions: {currentDir.FullName}", new(MessageType.Warning), cancellationToken);
        }
        catch (Exception ex)
        {
            // Using Error type as original was [red]
            await _userInteraction.NotifyAsync($"Error accessing {currentDir.FullName}: {ex.Message}", new(MessageType.Error), cancellationToken);
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
        var tree = new Tree("[yellow underline]Select Files/Folders (backspace to exit)[/]")
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