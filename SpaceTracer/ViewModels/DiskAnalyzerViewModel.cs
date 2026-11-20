using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinTracer.Core;
using WinTracer.Core.Native;
using WinTracer.Core.Scanning;
using WinTracer.Services;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public sealed partial class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskScanner _scanner;
    private readonly DispatcherTimer _progressTimer;
    private readonly Stopwatch _stopwatch = new();
    private CancellationTokenSource? _cts;
    private ScanProgress? _progress;

    public DiskAnalyzerViewModel(IDiskScanner scanner)
    {
        _scanner = scanner;
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        ShowElevationHint = !SystemShell.IsElevated;
        RefreshDrives();
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ObservableCollection<FolderNodeViewModel> RootNodes { get; } = [];
    public ObservableCollection<FsItemViewModel> Items { get; } = [];

    public bool IsElevated => SystemShell.IsElevated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand), nameof(ScanFolderCommand),
        nameof(DeleteToRecycleBinCommand), nameof(DeletePermanentlyCommand), nameof(MoveCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial bool ShowElevationHint { get; set; }

    [ObservableProperty]
    public partial string? ScanTarget { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial string? CurrentPath { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    partial void OnErrorMessageChanged(string? value) => HasError = value is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
    public partial FolderNodeViewModel? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial FsItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity NoticeSeverity { get; set; }

    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    [ObservableProperty]
    public partial bool IsFileOperationRunning { get; set; }

    partial void OnSelectedFolderChanged(FolderNodeViewModel? value) => RefreshItems();

    private void RefreshItems()
    {
        Items.Clear();
        var folder = SelectedFolder;
        if (folder?.Node.Children is not { } children) return;
        long total = folder.Node.Size;
        foreach (var child in children)
            Items.Add(new FsItemViewModel(child, total));
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        NoticeMessage = message;
        NoticeSeverity = severity;
        HasNotice = true;
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand]
    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                    Drives.Add(new DriveItemViewModel(d));
            }
            catch (IOException)
            {
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanDrive(DriveItemViewModel? drive) =>
        drive is null ? Task.CompletedTask : ScanAsync(drive.RootPath);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Disk.PickFolder") };
        if (dialog.ShowDialog() == true)
            await ScanAsync(dialog.FolderName);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task ScanAsync(string path)
    {
        SelectedFolder = null;
        RootNodes.Clear();
        Items.Clear();
        HasResult = false;
        ErrorMessage = null;
        Summary = null;
        GC.Collect();

        ScanTarget = path;
        _progress = new ScanProgress();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        _stopwatch.Restart();
        _progressTimer.Start();
        UpdateProgress();

        try
        {
            var root = await _scanner.ScanAsync(path, _progress, _cts.Token);
            _stopwatch.Stop();

            var rootVm = new FolderNodeViewModel(root, null);
            RootNodes.Add(rootVm);
            rootVm.IsExpanded = true;
            rootVm.IsSelected = true;
            SelectedFolder = rootVm;
            HasResult = true;

            Summary = L.F("Disk.Summary", root.FileCount, root.DirCount, ByteSize.Format(root.Size), ByteSize.Format(root.Allocated), _stopwatch.Elapsed.TotalSeconds);
            if (_progress.Errors > 0)
                Summary += L.F("Disk.SummaryDenied", _progress.Errors);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = L.T("Disk.Cancelled");
        }
        catch (Exception ex)
        {
            ErrorMessage = L.F("Disk.ScanFailed", path, ex.GetBaseException().Message);
        }
        finally
        {
            _progressTimer.Stop();
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgress()
    {
        if (_progress is null) return;
        ProgressText = L.F("Disk.Progress", _progress.Files, _progress.Directories, ByteSize.Format(_progress.Bytes), _stopwatch.Elapsed.TotalSeconds);
        CurrentPath = _progress.CurrentPath;
    }

    [RelayCommand]
    private void Open(FsItemViewModel? item)
    {
        if (item is null || SelectedFolder is null) return;

        if (!item.IsDirectory || item.IsReparsePoint)
        {
            SystemShell.ShowInExplorer(item.FullPath);
            return;
        }

        SelectedFolder.IsExpanded = true;
        var child = SelectedFolder.FindChild(item.Node);
        if (child is null) return;
        child.IsSelected = true;
        SelectedFolder = child;
    }

    private bool CanGoUp() => SelectedFolder?.Parent is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        var parent = SelectedFolder?.Parent;
        if (parent is null) return;
        parent.IsSelected = true;
        SelectedFolder = parent;
    }

    [RelayCommand]
    private void ShowInExplorer(object? target)
    {
        if (ResolveNodes(target).FirstOrDefault() is { } node)
            SystemShell.ShowInExplorer(node.FullPath);
    }

    private bool CanModify(object? target) => !IsFileOperationRunning && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanModify))]
    private Task DeleteToRecycleBin(object? target) => DeleteAsync(target, toRecycleBin: true);

    [RelayCommand(CanExecute = nameof(CanModify))]
    private Task DeletePermanently(object? target) => DeleteAsync(target, toRecycleBin: false);

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task Move(object? target)
    {
        var nodes = ResolveNodes(target);
        if (nodes.Count == 0 || !CheckSafety(nodes, L.T("Disk.VerbMove"), out string? warning)) return;

        var dialog = new OpenFolderDialog { Title = L.F("Disk.MoveWhere", Describe(nodes)) };
        if (dialog.ShowDialog() != true) return;
        string destination = dialog.FolderName;

        if (nodes.Any(n => PathSafety.IsSameOrUnder(destination, n.FullPath)))
        {
            ShowNotice(L.T("Disk.MoveIntoSelf"), InfoBarSeverity.Warning);
            return;
        }
        if (nodes.All(n => string.Equals(Path.GetDirectoryName(n.FullPath), Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase)))
        {
            ShowNotice(L.T("Disk.AlreadyThere"), InfoBarSeverity.Informational);
            return;
        }

        if (warning is not null &&
            !await Dialogs.ConfirmAsync(L.T("Disk.MoveTitle"), L.F("Disk.MoveAnyway", warning, Describe(nodes), destination), L.T("Disk.MoveButton")))
            return;

        var outcome = await RunFileOperationAsync(nodes,
            paths => ShellFileOperations.MoveAsync(paths, destination, Dialogs.MainWindowHandle));
        if (outcome.Removed.Count == 0) return;

        string text = L.F("Disk.Moved", Describe(outcome.Removed), destination);
        if (RootNodes.FirstOrDefault() is { } root && PathSafety.IsSameOrUnder(destination, root.Node.FullPath))
            text += L.T("Disk.MovedInside");
        ShowNotice(text, InfoBarSeverity.Success);
    }

    private async Task DeleteAsync(object? target, bool toRecycleBin)
    {
        var nodes = ResolveNodes(target);
        if (nodes.Count == 0 || !CheckSafety(nodes, L.T("Disk.VerbDelete"), out string? warning)) return;

        string what = Describe(nodes);
        string message = toRecycleBin
            ? L.F("Disk.RecycleConfirm", what)
            : L.F("Disk.DeleteConfirm", what);
        if (warning is not null) message = warning + "\n\n" + message;

        if (!await Dialogs.ConfirmAsync(toRecycleBin ? L.T("Disk.RecycleTitle") : L.T("Disk.DeleteTitle"),
                message, toRecycleBin ? L.T("Disk.RecycleButton") : L.T("Disk.DeleteButton"), destructive: true))
            return;

        var outcome = await RunFileOperationAsync(nodes,
            paths => ShellFileOperations.DeleteAsync(paths, toRecycleBin, Dialogs.MainWindowHandle));
        if (outcome.Removed.Count > 0)
        {
            ShowNotice(toRecycleBin
                ? L.F("Disk.Recycled", Describe(outcome.Removed))
                : L.F("Disk.Deleted", Describe(outcome.Removed)), InfoBarSeverity.Success);
        }
    }

    private sealed record FileOpOutcome(FileOperationOutcome Result, IReadOnlyList<FsNode> Removed);

    private async Task<FileOpOutcome> RunFileOperationAsync(
        IReadOnlyList<FsNode> nodes, Func<IReadOnlyList<string>, Task<FileOperationOutcome>> operation)
    {
        IsFileOperationRunning = true;
        NotifyFileCommands();
        try
        {
            var paths = nodes.Select(n => n.FullPath).ToList();
            var result = await operation(paths);

            var removed = nodes.Where(n => !File.Exists(n.FullPath) && !Directory.Exists(n.FullPath)).ToList();
            ApplyRemoval(removed);

            if (result == FileOperationOutcome.Failed && removed.Count < nodes.Count)
                ShowNotice(L.T("Disk.PartialFail"), InfoBarSeverity.Warning);
            return new FileOpOutcome(result, removed);
        }
        finally
        {
            IsFileOperationRunning = false;
            NotifyFileCommands();
        }
    }

    private void ApplyRemoval(IReadOnlyList<FsNode> removed)
    {
        if (removed.Count == 0 || RootNodes.FirstOrDefault() is not { } rootVm) return;

        foreach (var node in removed)
        {
            if (node.IsDirectory && node.Parent is { } parent && FindLoadedViewModel(rootVm, parent) is { } parentVm)
                parentVm.RemoveChild(node);
            node.DetachFromParent();
        }

        rootVm.RefreshTotals();

        var folder = SelectedFolder;
        while (folder is not null && !folder.Node.IsAttached) folder = folder.Parent;
        if (!ReferenceEquals(folder, SelectedFolder) && folder is not null)
        {
            folder.IsSelected = true;
            SelectedFolder = folder;
        }
        else
        {
            RefreshItems();
            OnPropertyChanged(nameof(SelectedFolder));
        }

        var root = rootVm.Node;
        Summary = L.F("Disk.SummaryUpdated", root.FileCount, root.DirCount, ByteSize.Format(root.Size), ByteSize.Format(root.Allocated));
    }

    private static FolderNodeViewModel? FindLoadedViewModel(FolderNodeViewModel rootVm, FsNode node)
    {
        var chain = new Stack<FsNode>();
        for (var n = node; n is not null && !ReferenceEquals(n, rootVm.Node); n = n.Parent)
            chain.Push(n);

        var vm = rootVm;
        while (chain.Count > 0 && vm is not null)
            vm = vm.FindLoadedChild(chain.Pop());
        return vm;
    }

    private static List<FsNode> ResolveNodes(object? target)
    {
        IEnumerable<FsNode> nodes = target switch
        {
            FsItemViewModel item => [item.Node],
            FolderNodeViewModel folder => [folder.Node],
            System.Collections.IList list => list.OfType<FsItemViewModel>().Select(i => i.Node),
            _ => [],
        };
        return nodes.Where(n => n.Parent is not null).Distinct().ToList();
    }

    private bool CheckSafety(IReadOnlyList<FsNode> nodes, string verb, out string? warning)
    {
        warning = null;
        foreach (var node in nodes)
        {
            var verdict = PathSafety.Check(node.FullPath);
            if (verdict.Risk == PathRisk.Blocked)
            {
                ShowNotice(L.F("Disk.Blocked", verb, node.FullPath, verdict.Reason), InfoBarSeverity.Error);
                return false;
            }
            if (verdict.Risk == PathRisk.Warning) warning ??= verdict.Reason;
        }
        return true;
    }

    private static string Describe(IReadOnlyList<FsNode> nodes)
    {
        long size = nodes.Sum(n => n.Size);
        return nodes.Count == 1
            ? L.F("Disk.OneItem", nodes[0].Name, ByteSize.Format(size))
            : L.F("Disk.ManyItems", nodes.Count, ByteSize.Format(size));
    }

    private void NotifyFileCommands()
    {
        DeleteToRecycleBinCommand.NotifyCanExecuteChanged();
        DeletePermanentlyCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenCurrentFolder()
    {
        if (SelectedFolder is not null) SystemShell.OpenFolder(SelectedFolder.Node.FullPath);
    }

    [RelayCommand]
    private void CopyPath(object? target)
    {
        var paths = ResolveNodes(target).Select(n => n.FullPath).ToList();
        if (paths.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();

    [RelayCommand]
    private void DismissElevationHint() => ShowElevationHint = false;
}
