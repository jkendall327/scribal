namespace Scribal.Cli;

public class FileSystemNode
{
    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public List<FileSystemNode> Children { get; } = [];
    public FileSystemNode? Parent { get; private set; }

    public bool IsSelected { get; set; }
    public bool IsExpanded { get; set; }

    public FileSystemNode(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
        Name = System.IO.Path.GetFileName(path);
        
        if (string.IsNullOrEmpty(Name) && isDirectory)
        {
            Name = path;
        }
    }

    public void AddChild(FileSystemNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}