using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTracer.Core.Startup;
using WinTracer.Services;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public enum StartupFilter
{
    All,
    Enabled,
    Disabled,
    Problems,
}

public sealed partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupViewModel _owner;
    private bool _suppress;

    public StartupItemViewModel(StartupEntry entry, StartupViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        _suppress = true;
        IsEnabled = entry.IsEnabled;
        _suppress = false;
        Icon = FileIcons.Get(entry.ExecutablePath);
    }

    public StartupEntry Entry { get; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Location => Entry.Location;
    public string? ExecutablePath => Entry.ExecutablePath;
    public bool FileMissing => !Entry.FileExists;
    public bool CanToggle => !Entry.RequiresAdmin || SystemShell.IsElevated;
    public bool NeedsAdmin => !CanToggle;
    public bool CanDelete => Entry.CanDelete && CanToggle;
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon is not null;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Entry.Publisher is { } p) parts.Add(p);
            else if (!FileMissing) parts.Add(L.T("Startup.NoPublisher"));
            if (Entry.Description is { } d && !string.Equals(d, Name, StringComparison.OrdinalIgnoreCase)) parts.Add(d);
            parts.Add(Entry.SourceText);
            return string.Join(" · ", parts);
        }
    }

    public string GroupTitle => Entry.Kind switch
    {
        StartupKind.Registry or StartupKind.StartupFolder => L.T("Startup.Group.Programs"),
        StartupKind.ScheduledTask => L.T("Startup.Group.Tasks"),
        StartupKind.Service => L.T("Startup.Group.Services"),
        _ => L.T("Startup.Group.Other"),
    };

    public int GroupOrder => Entry.Kind switch
    {
        StartupKind.Registry or StartupKind.StartupFolder => 0,
        StartupKind.ScheduledTask => 1,
        _ => 2,
    };

    public SymbolRegular FallbackIcon => Entry.Kind switch
    {
        StartupKind.ScheduledTask => SymbolRegular.CalendarClock20,
        StartupKind.Service => SymbolRegular.Settings20,
        _ => SymbolRegular.AppGeneric20,
    };

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress) return;
        try
        {
            Entry.SetEnabled(value);
            _owner.OnItemToggled(this);
        }
        catch (Exception ex)
        {
            _suppress = true;
            IsEnabled = !value;
            _suppress = false;
            _owner.ReportError(L.F("Startup.ToggleFailed", value ? L.T("Startup.VerbEnable") : L.T("Startup.VerbDisable"), Name, Describe(ex)));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException or System.Security.SecurityException => L.T("Common.NoAccess"),
        System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80070005) } => L.T("Common.NoAccess"),
        _ => ex.GetBaseException().Message,
    };
}

public sealed partial class StartupViewModel : ObservableObject
{
    private static readonly string BackupFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTracer", "Backups");

    private bool _loadedOnce;

    public StartupViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Items);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StartupItemViewModel.GroupTitle)));
        View.SortDescriptions.Add(new SortDescription(nameof(StartupItemViewModel.GroupOrder), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(StartupItemViewModel.Name), ListSortDirection.Ascending));
        View.Filter = Matches;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];
    public ICollectionView View { get; }

    public bool IsElevated => SystemShell.IsElevated;

    public IReadOnlyList<KeyValuePair<StartupFilter, string>> Filters { get; } =
    [
        new(StartupFilter.All, L.T("Startup.Filter.All")),
        new(StartupFilter.Enabled, L.T("Startup.Filter.Enabled")),
        new(StartupFilter.Disabled, L.T("Startup.Filter.Disabled")),
        new(StartupFilter.Problems, L.T("Startup.Filter.Problems")),
    ];

    [ObservableProperty]
    public partial StartupFilter Filter { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    partial void OnFilterChanged(StartupFilter value) => View.Refresh();
    partial void OnSearchTextChanged(string? value) => View.Refresh();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity NoticeSeverity { get; set; }

    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowElevationHint { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await Refresh();
    }

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        IsLoading = true;
        try
        {
            var result = await RunOnStaThread(StartupScanner.Scan);
            Items.Clear();
            foreach (var entry in result.Entries)
                Items.Add(new StartupItemViewModel(entry, this));

            ShowElevationHint = !IsElevated && Items.Any(i => i.NeedsAdmin);
            if (result.Errors.Count > 0)
                Show(L.F("Startup.SourcesFailed", string.Join("; ", result.Errors)), InfoBarSeverity.Warning);
            UpdateSummary();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool Matches(object obj)
    {
        if (obj is not StartupItemViewModel item) return false;
        bool stateOk = Filter switch
        {
            StartupFilter.Enabled => item.IsEnabled,
            StartupFilter.Disabled => !item.IsEnabled,
            StartupFilter.Problems => item.FileMissing,
            _ => true,
        };
        if (!stateOk) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        string q = SearchText.Trim();
        return item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Command.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    internal void OnItemToggled(StartupItemViewModel item)
    {
        UpdateSummary();
        if (Filter is StartupFilter.Enabled or StartupFilter.Disabled) View.Refresh();
        Show(item.IsEnabled
                ? L.F("Startup.Enabled", item.Name)
                : L.F("Startup.Disabled", item.Name),
            InfoBarSeverity.Success);
    }

    internal void ReportError(string message) => Show(message, InfoBarSeverity.Error);

    private void Show(string message, InfoBarSeverity severity)
    {
        NoticeMessage = message;
        NoticeSeverity = severity;
        HasNotice = true;
    }

    private void UpdateSummary()
    {
        int enabled = Items.Count(i => i.IsEnabled);
        int missing = Items.Count(i => i.FileMissing);
        Summary = L.F("Startup.Summary", enabled, Items.Count) + (missing > 0 ? L.F("Startup.SummaryBroken", missing) : "");
    }

    [RelayCommand]
    private static void OpenLocation(StartupItemViewModel? item)
    {
        if (item?.ExecutablePath is { } path && File.Exists(path))
            SystemShell.ShowInExplorer(path);
    }

    [RelayCommand]
    private async Task Delete(StartupItemViewModel? item)
    {
        if (item is null || !item.CanDelete) return;

        string message = L.F("Startup.DeleteConfirm", item.Name, item.Command) +
                         (item.FileMissing
                             ? L.T("Startup.DeleteMissing")
                             : L.T("Startup.DeleteHint")) +
                         L.F("Startup.DeleteBackup", BackupFolder);
        if (!await Dialogs.ConfirmAsync(L.T("Startup.DeleteTitle"), message, L.T("Common.Delete"), destructive: true))
            return;

        try
        {
            item.Entry.Delete(BackupFolder);
            Items.Remove(item);
            UpdateSummary();
            Show(L.F("Startup.Deleted", item.Name, BackupFolder), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ReportError(L.F("Startup.DeleteFailed", item.Name, ex.GetBaseException().Message));
        }
    }

    [RelayCommand]
    private static void OpenBackups()
    {
        Directory.CreateDirectory(BackupFolder);
        SystemShell.OpenFolder(BackupFolder);
    }

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();

    private static Task<T> RunOnStaThread<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
