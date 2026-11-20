namespace WinTracer.Core.Scanning;

public sealed class FsNode
{
    internal FsNode(string name, FsNode? parent, FileAttributes attributes, DateTime lastWriteUtc)
    {
        Name = name;
        Parent = parent;
        Attributes = attributes;
        LastWriteUtc = lastWriteUtc;
    }

    public string Name { get; }
    public FsNode? Parent { get; }
    public FileAttributes Attributes { get; }
    public DateTime LastWriteUtc { get; }

    public long Size { get; internal set; }

    public long Allocated { get; internal set; }

    public int FileCount { get; internal set; }

    public int DirCount { get; internal set; }

    public bool AccessDenied { get; internal set; }

    public List<FsNode>? Children { get; internal set; }

    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    public bool HasSubdirectories
    {
        get
        {
            if (Children is null) return false;
            foreach (var c in Children)
                if (c.IsDirectory) return true;
            return false;
        }
    }

    public string FullPath => Parent is null ? Name : Path.Join(Parent.FullPath, Name);

    public void DetachFromParent()
    {
        var parent = Parent;
        if (parent?.Children is null || !parent.Children.Remove(this)) return;

        long size = Size, allocated = Allocated;
        int files = IsDirectory ? FileCount : 1;
        int dirs = IsDirectory ? DirCount + 1 : 0;

        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Size -= size;
            p.Allocated -= allocated;
            p.FileCount -= files;
            p.DirCount -= dirs;
        }
    }

    public bool IsAttached
    {
        get
        {
            for (var n = this; n.Parent is not null; n = n.Parent)
            {
                if (n.Parent.Children is null || !n.Parent.Children.Contains(n))
                    return false;
            }
            return true;
        }
    }

    public override string ToString() => FullPath;
}
