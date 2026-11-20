using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTracer.Core.RegistryTools;
using WinTracer.Services;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public sealed partial class RegistryIssueViewModel : ObservableObject
{
    private readonly Action _changed;

    public RegistryIssueViewModel(RegistryIssue issue, bool selected, Action changed)
    {
        Issue = issue;
        _changed = changed;
        CanSelect = !issue.RequiresAdmin || SystemShell.IsElevated;
        IsSelected = selected && CanSelect;
    }

    public RegistryIssue Issue { get; }
    public string Title => Issue.Title;
    public string MissingText => L.F("Registry.Missing", Issue.MissingTarget);
    public string Location => Issue.Location;
    public bool CanSelect { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

public sealed partial class RegistryCategoryViewModel : ObservableObject
{
    private bool _bulk;

    public RegistryCategoryViewModel(RegistryIssueCategory category, IEnumerable<RegistryIssue> issues, Action changed)
    {
        Category = category;
        bool byDefault = RegistryScanner.IsSelectedByDefault(category);
        foreach (var issue in issues.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase))
            Items.Add(new RegistryIssueViewModel(issue, byDefault, () => { if (!_bulk) { OnPropertyChanged(nameof(IsAllSelected)); changed(); } }));
    }

    public RegistryIssueCategory Category { get; }
    public ObservableCollection<RegistryIssueViewModel> Items { get; } = [];
    public string Title => RegistryScanner.CategoryTitle(Category);
    public string Description => RegistryScanner.CategoryDescription(Category);
    public int Count => Items.Count;
    public bool NeedsAdmin => Items.Any(i => !i.CanSelect);

    public bool? IsAllSelected
    {
        get
        {
            var selectable = Items.Where(i => i.CanSelect).ToList();
            if (selectable.Count == 0 || selectable.All(i => !i.IsSelected)) return false;
            return selectable.All(i => i.IsSelected) ? true : null;
        }
        set
        {
            bool target = value ?? false;
            _bulk = true;
            foreach (var item in Items.Where(i => i.CanSelect)) item.IsSelected = target;
            _bulk = false;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    internal Action? Changed { get; set; }
}

public sealed partial class RegistryViewModel : ObservableObject
{
    private bool _scannedOnce;

    public ObservableCollection<RegistryCategoryViewModel> Categories { get; } = [];

    public bool IsElevated => SystemShell.IsElevated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(FixCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? BusyText { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    public partial bool CreateRestorePoint { get; set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity NoticeSeverity { get; set; }

    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowElevationHint { get; set; }

    public RegistryViewModel()
    {
        CreateRestorePoint = SystemShell.IsElevated;
    }

    public async Task EnsureScannedAsync()
    {
        if (_scannedOnce) return;
        _scannedOnce = true;
        await Scan();
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Scan()
    {
        IsBusy = true;
        BusyText = L.T("Registry.Checking");
        try
        {
            var result = await Task.Run(() => RegistryScanner.Scan());
            Categories.Clear();
            foreach (var group in result.Issues.GroupBy(i => i.Category).OrderBy(g => g.Key))
            {
                var cat = new RegistryCategoryViewModel(group.Key, group, UpdateSummary);
                cat.Changed = UpdateSummary;
                Categories.Add(cat);
            }
            HasResults = Categories.Count > 0;
            ShowElevationHint = !IsElevated && Categories.Any(c => c.NeedsAdmin);
            if (result.Errors.Count > 0)
                Show(L.F("Registry.SectionsFailed", string.Join("; ", result.Errors)), InfoBarSeverity.Warning);
            UpdateSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Fix()
    {
        var selected = Categories.SelectMany(c => c.Items).Where(i => i.IsSelected && i.CanSelect).Select(i => i.Issue).ToList();
        if (selected.Count == 0) return;

        string breakdown = string.Join("\n", selected.GroupBy(i => i.Category)
            .Select(g => $"• {RegistryScanner.CategoryTitle(g.Key)}: {g.Count()}"));
        string message = L.F("Registry.ConfirmText", selected.Count, breakdown, RegistryFixer.BackupFolder) +
                         (CreateRestorePoint ? L.T("Registry.ConfirmRestorePoint") : "");
        if (!await Dialogs.ConfirmAsync(L.T("Registry.ConfirmTitle"), message, L.T("Registry.ConfirmButton"), destructive: true))
            return;

        IsBusy = true;
        try
        {
            if (CreateRestorePoint && IsElevated)
            {
                BusyText = L.T("Registry.CreatingRestorePoint");
                string? error = await RegistryFixer.CreateRestorePointAsync(L.T("Registry.RestorePointName"));
                if (error is not null &&
                    !await Dialogs.ConfirmAsync(L.T("Registry.RestorePointFailedTitle"),
                        L.F("Registry.RestorePointFailed", error), L.T("Common.Continue")))
                {
                    return;
                }
            }

            BusyText = L.T("Registry.Removing");
            var result = await Task.Run(() => RegistryFixer.Fix(selected));

            string text = L.F("Registry.Fixed", result.Fixed) +
                          (result.Failed > 0 ? L.F("Registry.FixFailed", result.Failed, string.Join("; ", result.Errors)) : "") +
                          L.F("Registry.Backup", result.BackupFile);
            await Scan();
            Show(text, result.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show(L.F("Registry.NothingChanged", ex.GetBaseException().Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenBackups()
    {
        Directory.CreateDirectory(RegistryFixer.BackupFolder);
        SystemShell.OpenFolder(RegistryFixer.BackupFolder);
    }

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();

    private void Show(string message, InfoBarSeverity severity)
    {
        NoticeMessage = message;
        NoticeSeverity = severity;
        HasNotice = true;
    }

    private void UpdateSummary()
    {
        int total = Categories.Sum(c => c.Count);
        int selected = Categories.SelectMany(c => c.Items).Count(i => i.IsSelected && i.CanSelect);
        Summary = total == 0 ? L.T("Registry.NoProblems") : L.F("Registry.Summary", total, selected);
        FixCommand.NotifyCanExecuteChanged();
    }
}
