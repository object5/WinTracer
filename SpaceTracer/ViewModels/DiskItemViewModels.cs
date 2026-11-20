using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinTracer.Core;
using WinTracer.Core.Scanning;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public sealed class DriveItemViewModel
{
    public DriveItemViewModel(DriveInfo drive)
    {
        RootPath = drive.RootDirectory.FullName;
        string letter = RootPath.TrimEnd('\\');
        string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? drive.DriveType == DriveType.Removable ? L.T("Disk.RemovableDrive") : L.T("Disk.LocalDrive")
            : drive.VolumeLabel;

        Title = $"{label} ({letter})";
        Format = drive.DriveFormat;
        TotalSize = drive.TotalSize;
        FreeSpace = drive.TotalFreeSpace;
        UsedPercent = TotalSize > 0 ? 100d * (TotalSize - FreeSpace) / TotalSize : 0;
        IsSystem = string.Equals(letter, Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    public string RootPath { get; }
    public string Title { get; }
    public string Format { get; }
    public long TotalSize { get; }
    public long FreeSpace { get; }
    public double UsedPercent { get; }
    public bool IsSystem { get; }
    public bool IsLowSpace => TotalSize > 0 && FreeSpace < TotalSize / 10;

    public string UsageText => L.F("Disk.Usage", ByteSize.Format(FreeSpace), ByteSize.Format(TotalSize));
    public string Details => IsSystem ? L.F("Disk.SystemDrive", Format) : Format;
}

public sealed partial class FolderNodeViewModel : ObservableObject
{
    private static readonly FolderNodeViewModel LoadingPlaceholder = new();
    private bool _childrenLoaded;

    private FolderNodeViewModel()
    {
        Node = new FsNode("…", null, FileAttributes.Directory, default);
        _childrenLoaded = true;
    }

    public FolderNodeViewModel(FsNode node, FolderNodeViewModel? parent)
    {
        Node = node;
        Parent = parent;
        long parentSize = parent?.Node.Size ?? node.Size;
        PercentOfParent = parentSize > 0 ? 100d * node.Size / parentSize : 0;

        if (node.HasSubdirectories)
            Children.Add(LoadingPlaceholder);
        else
            _childrenLoaded = true;
    }

    public FsNode Node { get; }
    public FolderNodeViewModel? Parent { get; }
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    public string Name => Parent is null ? Node.FullPath : Node.Name;
    public long Size => Node.Size;
    public double PercentOfParent { get; }
    public bool AccessDenied => Node.AccessDenied;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureChildren();
    }

    public void EnsureChildren()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in Node.Children!)
        {
            if (child.IsDirectory)
                Children.Add(new FolderNodeViewModel(child, this));
        }
    }

    public FolderNodeViewModel? FindChild(FsNode node)
    {
        EnsureChildren();
        return Children.FirstOrDefault(c => ReferenceEquals(c.Node, node));
    }

    public FolderNodeViewModel? FindLoadedChild(FsNode node) =>
        _childrenLoaded ? Children.FirstOrDefault(c => ReferenceEquals(c.Node, node)) : null;

    public void RemoveChild(FsNode node)
    {
        if (FindLoadedChild(node) is { } vm) Children.Remove(vm);
        if (!_childrenLoaded && !Node.HasSubdirectories)
        {
            Children.Clear();
            _childrenLoaded = true;
        }
    }

    public void RefreshTotals()
    {
        OnPropertyChanged(nameof(Size));
        if (!_childrenLoaded) return;
        foreach (var child in Children) child.RefreshTotals();
    }
}

public sealed class FsItemViewModel(FsNode node, long parentSize)
{
    public FsNode Node { get; } = node;
    public string Name => Node.Name;
    public string FullPath => Node.FullPath;
    public bool IsDirectory => Node.IsDirectory;
    public bool IsReparsePoint => Node.IsReparsePoint;
    public bool AccessDenied => Node.AccessDenied;
    public long Size => Node.Size;
    public long Allocated => Node.Allocated;
    public double Percent { get; } = parentSize > 0 ? 100d * node.Size / parentSize : 0;
    public string PercentText => Percent >= 0.1 ? $"{Percent:0.0} %" : "< 0.1 %";
    public double BarWidth => Math.Max(Percent > 0 ? 2 : 0, 60 * Percent / 100);
    public int? FileCount => IsDirectory ? Node.FileCount : null;
    public DateTime LastWrite => Node.LastWriteUtc.ToLocalTime();

    public string Kind =>
        IsReparsePoint ? L.T("Disk.Kind.Link") :
        IsDirectory ? L.T("Disk.Kind.Folder") :
        Path.GetExtension(Name) is { Length: > 1 } ext ? ext[1..].ToUpperInvariant() : L.T("Disk.Kind.File");

    public SymbolRegular Icon =>
        AccessDenied ? SymbolRegular.FolderProhibited20 :
        IsReparsePoint ? SymbolRegular.LinkSquare20 :
        IsDirectory ? SymbolRegular.Folder20 :
        SymbolRegular.Document20;
}
