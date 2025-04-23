namespace Scribal.Cli;

using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class StickyTreeSelector
{
    private readonly FileSystemNode _root;
    private List<FileSystemNode> _visibleNodes;
    private int _currentNodeIndex;

    // --- Configuration ---
    private const string SelectedPrefix = "[green][*][/]";
    private const string UnselectedPrefix = "[dim][ ][/]";
    private const string ExpandedPrefix = "v "; // Or use Emoji.Known.OpenFolder
    private const string CollapsedPrefix = "> "; // Or use Emoji.Known.ClosedFolder
    private const string FileIcon = "  "; // Or use Emoji.Known.PageFacingUp

    // --- Constructor ---
    // Option 1: Build from a starting path
    public StickyTreeSelector(string rootPath)
    {
        _root = BuildFileSystemTree(rootPath);
        _root.IsExpanded = true; // Start with root expanded
        _visibleNodes = GetVisibleNodes(_root);
        _currentNodeIndex = 0; // Start at the root
    }

    // Option 2: Build from your pre-existing Tree structure
    // public StickyTreeSelector(YourFileSystemTree yourRoot)
    // {
    //     _root = ConvertToUiNodes(yourRoot, null); // You'll need this conversion method
    //     _root.IsExpanded = true; // Start with root expanded
    //     _visibleNodes = GetVisibleNodes(_root);
    //     _currentNodeIndex = 0; // Start at the root
    // }

    // --- Public Method to Start Selection ---
    public List<string> GetSelectedPaths()
    {
        Console.Clear(); // Start clean
        List<string> selectedPaths = new List<string>();

        AnsiConsole.Live(new Markup(Markup.Escape(_root.Name))) // Initial title, will be replaced
            .Start(ctx =>
            {
                while (true)
                {
                    // 1. Re-calculate visible nodes (important if expansion changed)
                    _visibleNodes = GetVisibleNodes(_root);
                    if (_currentNodeIndex >= _visibleNodes.Count)
                    {
                        _currentNodeIndex = Math.Max(0, _visibleNodes.Count - 1);
                    }

                    // 2. Build the Spectre Tree for display
                    var treeWidget = BuildSpectreTreeWidget();
                    ctx.UpdateTarget(treeWidget); // Render the tree
                    ctx.Refresh(); // Ensure update

                    // 3. Wait for and process input
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true); // Don't echo key

                    if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        break; // Exit loop
                    }

                    bool stateChanged = HandleKeyPress(keyInfo);

                    // No need to manually refresh here, the loop continues
                    // and ctx.UpdateTarget handles the redraw on the next iteration.
                }
            });

        // After loop exits, collect selected paths
        selectedPaths = CollectSelectedPaths(_root);
        Console.Clear(); // Clean up after exit
        AnsiConsole.MarkupLine("[green]Selection complete.[/]");
        return selectedPaths;
    }

    // --- Input Handling ---
    private bool HandleKeyPress(ConsoleKeyInfo keyInfo)
    {
        if (_visibleNodes.Count == 0) return false; // Nothing to do

        FileSystemNode currentNode = _visibleNodes[_currentNodeIndex];
        bool needsRedraw = false;

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
                bool newSelectedState = !currentNode.IsSelected;

                // Apply the new state recursively starting from the current node
                SetSelectionRecursive(currentNode, newSelectedState);

                needsRedraw = true; // Signal that the display needs updating
                break; // Added break statement, essential for switch cases!

            case ConsoleKey.RightArrow:
                if (currentNode.IsDirectory && !currentNode.IsExpanded)
                {
                    currentNode.IsExpanded = true;
                    needsRedraw = true; // Expansion state changed, rebuild visible list
                }

                break;

            case ConsoleKey.LeftArrow:
                if (currentNode.IsDirectory && currentNode.IsExpanded)
                {
                    currentNode.IsExpanded = false;
                    needsRedraw = true; // Expansion state changed, rebuild visible list
                }

                // Optional: Navigate to parent if already collapsed or a file
                // else if (currentNode.Parent != null)
                // {
                //     int parentIndex = _visibleNodes.IndexOf(currentNode.Parent);
                //     if (parentIndex >= 0) _currentNodeIndex = parentIndex;
                //     needsRedraw = true;
                // }
                break;
        }

        return needsRedraw; // Indicate if the display needs updating
    }

    private void SetSelectionRecursive(FileSystemNode node, bool isSelected)
    {
        node.IsSelected = isSelected;

        // No need to check IsDirectory here, as files have empty Children lists.
        foreach (var child in node.Children)
        {
            SetSelectionRecursive(child, isSelected); // Recurse
        }
    }

    // --- Tree Building & Traversal ---

    // Build initial FileSystemNode tree from path (example implementation)
    private FileSystemNode BuildFileSystemTree(string path)
    {
        DirectoryInfo dirInfo = new DirectoryInfo(path);
        if (!dirInfo.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");

        var rootNode = new FileSystemNode(dirInfo.FullName, true);
        BuildTreeRecursive(rootNode, dirInfo);
        return rootNode;
    }

    private void BuildTreeRecursive(FileSystemNode parentNode, DirectoryInfo currentDir)
    {
        // Add Directories
        try
        {
            foreach (var dir in currentDir.GetDirectories())
            {
                var childDirNode = new FileSystemNode(dir.FullName, true);
                parentNode.AddChild(childDirNode);
                BuildTreeRecursive(childDirNode, dir); // Recurse
            }

            // Add Files (only .md)
            foreach (var file in currentDir.GetFiles("*.md"))
            {
                var childFileNode = new FileSystemNode(file.FullName, false);
                parentNode.AddChild(childFileNode);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories/files we don't have access to
            // Optionally add a node indicating access denied
            var inaccessibleNode = new FileSystemNode(Path.Combine(parentNode.Path, "[Access Denied]"), false);
            // Mark it visually later? Or just ignore.
            // parentNode.AddChild(inaccessibleNode);
        }
        catch (Exception ex) // Catch other potential IO errors
        {
            AnsiConsole.MarkupLine($"[red]Error accessing {currentDir.FullName}: {ex.Message}[/]");
            // Maybe add an error node?
        }
    }

    // Get flat list of currently visible nodes based on expansion state
    private List<FileSystemNode> GetVisibleNodes(FileSystemNode node)
    {
        var list = new List<FileSystemNode>();
        AddVisibleNodesRecursive(node, list);
        return list;
    }

    private void AddVisibleNodesRecursive(FileSystemNode node, List<FileSystemNode> list)
    {
        list.Add(node);
        if (node.IsDirectory && node.IsExpanded)
        {
            // Sort children for consistent order (optional)
            var sortedChildren = node.Children.OrderBy(c => !c.IsDirectory) // Dirs first
                .ThenBy(c => c.Name); // Then by name

            foreach (var child in sortedChildren)
            {
                AddVisibleNodesRecursive(child, list);
            }
        }
    }

    // --- Spectre Tree Widget Generation ---
    private Tree BuildSpectreTreeWidget()
    {
        var tree = new Tree($"[yellow underline]Select Files/Folders (.md only)[/]");
        tree.Guide = TreeGuide.Line; // Or Ascii, BoldLine, DoubleLine

        // Start recursion from the root
        int globalNodeIndex = 0; // Keep track of the linear index across recursion
        AddNodeToSpectreTree(_root, tree, ref globalNodeIndex);

        return tree;
    }

    private void AddNodeToSpectreTree(FileSystemNode node, IHasTreeNodes parentSpectreNode, ref int globalNodeIndex)
    {
        // Determine if this node is the currently highlighted one
        bool isCurrentNode = (_visibleNodes.Count > _currentNodeIndex) && (_visibleNodes[_currentNodeIndex] == node);

        // --- Refactored Label Construction ---

        // 1. Define Markers & Icons (plain text or Emoji)
        const string SelectedMarker = "*"; // Use Emoji.Known.CheckMark?
        const string UnselectedMarker = " ";
        string icon = node.IsDirectory
            ? (node.IsExpanded ? ExpandedPrefix : CollapsedPrefix) // Use constants like "> ", "v " or Emojis
            : FileIcon; // Use constant like "  " or Emoji

        // 2. Get the node name (escaped)
        string name = Markup.Escape(node.Name);

        // 3. Build the core text content
        string baseLabelText = $"{(node.IsSelected ? SelectedMarker : UnselectedMarker)} {icon}{name}";

        // 4. Apply styling based on state (wrap the base text)
        string finalLabelMarkup;
        if (isCurrentNode)
        {
            // Highlight takes precedence. Apply bold *inside* if also selected.
            string styledBase = node.IsSelected ? $"[bold]{baseLabelText}[/]" : baseLabelText;
            finalLabelMarkup = $"[underline yellow on blue]{styledBase}[/]";
        }
        else if (node.IsSelected)
        {
            // Selected but not current: Apply bold style
            finalLabelMarkup = $"[bold]{baseLabelText}[/]";
        }
        else
        {
            // Not selected, not current: Apply dim style (optional, remove [dim]...[/] if too faint)
            finalLabelMarkup = $"[dim]{baseLabelText}[/]";
        }
        // --- End Refactored Label Construction ---

        // Add the node to the Spectre Tree using the final markup string
        var treeNode = parentSpectreNode.AddNode(finalLabelMarkup);

        // Check if this specific node instance is actually visible in the flattened list
        bool isNodeVisibleInList = globalNodeIndex < _visibleNodes.Count && _visibleNodes[globalNodeIndex] == node;
        globalNodeIndex++; // Increment linear index *after* processing this node

        if (node.IsDirectory && node.IsExpanded && node.Children.Any() && isNodeVisibleInList)
        {
            var sortedChildren = node.Children.OrderBy(c => !c.IsDirectory).ThenBy(c => c.Name);
            foreach (var child in sortedChildren)
            {
                AddNodeToSpectreTree(child, treeNode, ref globalNodeIndex);
            }
        }
        // Optional placeholder for collapsed nodes (no change needed here)
        // else if (node.IsDirectory && !node.IsExpanded && node.Children.Any() && isNodeVisibleInList) { ... }
    }

    // --- Result Collection ---
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
            // If a directory is selected, we might not want to add its children individually
            // depending on the use case. Let's assume selecting a dir means the dir itself.
            // If you want all *files* within selected dirs, you'd modify this.
        }

        // Even if the parent is selected, check children unless requirement changes
        foreach (var child in node.Children)
        {
            CollectSelectedRecursive(child, selectedList);
        }
    }

    // --- Helper to convert Your Tree to UI Tree (If needed) ---
    // private FileSystemNode ConvertToUiNodes(YourFileSystemTree sourceNode, FileSystemNode parentUiNode)
    // {
    //     var uiNode = new FileSystemNode(sourceNode.Path, sourceNode.IsDirectory)
    //     {
    //         Parent = parentUiNode
    //         // Copy other relevant data if needed
    //     };
    //
    //     foreach (var sourceChild in sourceNode.Children)
    //     {
    //         uiNode.AddChild(ConvertToUiNodes(sourceChild, uiNode));
    //     }
    //     return uiNode;
    // }
}