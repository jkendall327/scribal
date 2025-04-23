namespace Scribal.Cli;

using System.Collections.Generic;
using System.IO; // For Path manipulation

public class FileSystemNode
{
    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public List<FileSystemNode> Children { get; } = new List<FileSystemNode>();
    public FileSystemNode? Parent { get; private set; } // Useful for navigation

    // --- UI State ---
    public bool IsSelected { get; set; } = false;
    public bool IsExpanded { get; set; } = false; // Start collapsed

    // Reference to your original data if needed
    // public YourFileSystemTree OriginalData { get; }

    public FileSystemNode(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
        Name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name) && isDirectory) // Handle root drive case C:\
        {
            Name = path;
        }
        // Set IsExpanded = true for the root or initial view if desired
    }

    public void AddChild(FileSystemNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}