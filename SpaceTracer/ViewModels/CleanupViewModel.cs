using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTracer.Core;
using WinTracer.Core.Cleanup;
using WinTracer.Services;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public enum CleanupItemState
{
    Idle,
    Analyzing,
    Ready,
    Cleaning,
    Done,
    Failed,
}

public sealed partial class CleanupItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public CleanupItemViewModel(CleanupTarget target, Action selectionChanged)
    {
        Target = target;
        _selectionChanged = selectionChanged;
        CanSelect = !target.RequiresAdmin || SystemShell.IsElevated;
        IsSelected = target.SelectedByDefault && CanSelect;
    }

    public CleanupTarget Target { get; }
    public string Title => Target.Title;
    public string Description => Target.Description;
    public string? Warning => Target.Warning;
    public string GroupTitle => CleanupCatalog.GroupTitle(Target.Group);
    public bool CanSelect { get; }
    public bool NeedsAdmin => !CanSelect;

    public SymbolRegular Icon => Target.Group switch
    {
        CleanupGroup.System => SymbolRegular.WindowApps20,
        CleanupGroup.Browsers => SymbolRegular.Globe20,
        CleanupGroup.Applications => SymbolRegular.Apps20,
        CleanupGroup.AiTools => SymbolRegular.BrainCircuit20,
        CleanupGroup.Development => SymbolRegular.Code20,
        _ => SymbolRegular.Broom20,
    };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial CleanupItemState State { get; set; }

    public bool IsBusy => State is CleanupItemState.Analyzing or CleanupItemState.Cleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    public partial long Bytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    public partial int Files { get; set; }

    public string SizeText => State switch
    {
        CleanupItemState.Analyzing => L.T("Cleanup.Analyzing"),
        CleanupItemState.Cleaning => L.T("Cleanup.Cleaning"),
        CleanupItemState.Idle => "",
        _ when Bytes < 0 => "—",
        _ when Bytes == 0 => L.T("Cleanup.Empty"),
        _ => ByteSize.Format(Bytes),
    };

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? RunningText { get; set; }

    partial void OnStateChanged(CleanupItemState value) => OnPropertyChanged(nameof(SizeText));
}

public sealed partial class CleanupViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private bool _analyzedOnce;

    public CleanupViewModel()
    {
        foreach (var target in CleanupCatalog.Create())
            _all.Add(new CleanupItemViewModel(target, UpdateTotals));
    }

    private readonly List<CleanupItemViewModel> _all = [];

    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    public bool IsElevated => SystemShell.IsElevated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(CleanCommand), nameof(SelectRecommendedCommand), nameof(SelectNoneCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? SelectedSummary { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial string? ResultMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ResultSeverity { get; set; }

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial bool ShowElevationHint { get; set; }

    public async Task EnsureAnalyzedAsync()
    {
        if (_analyzedOnce || IsBusy) return;
        _analyzedOnce = true;
        await Analyze();
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Analyze()
    {
        IsBusy = true;
        HasResult = false;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            ProgressText = L.T("Cleanup.Searching");
            var present = await Task.Run(() => _all.Where(i => SafePresent(i.Target)).ToList(), ct);

            Items.Clear();
            foreach (var item in present)
            {
                item.State = CleanupItemState.Analyzing;
                item.StatusText = null;
                Items.Add(item);
            }
            ShowElevationHint = !IsElevated && Items.Any(i => i.NeedsAdmin);

            await Task.WhenAll(Items.Select(i => AnalyzeItemAsync(i, ct)));
            ProgressText = null;
        }
        catch (OperationCanceledException)
        {
            ProgressText = L.T("Cleanup.AnalyzeCancelled");
        }
        finally
        {
            IsBusy = false;
            UpdateTotals();
        }
    }

    private static bool SafePresent(CleanupTarget t)
    {
        try { return t.IsPresent(); }
        catch { return false; }
    }

    private static async Task AnalyzeItemAsync(CleanupItemViewModel item, CancellationToken ct)
    {
        try
        {
            var estimate = await item.Target.AnalyzeAsync(ct);
            item.Bytes = estimate.Bytes;
            item.Files = estimate.Files;
            item.StatusText = estimate.Note;
            item.RunningText = await Task.Run(item.Target.GetRunningProcesses, ct) is { } running
                ? L.F("Cleanup.Running", running)
                : null;
            item.State = CleanupItemState.Ready;
        }
        catch (OperationCanceledException)
        {
            item.State = CleanupItemState.Idle;
        }
        catch (Exception ex)
        {
            item.Bytes = -1;
            item.StatusText = ex.GetBaseException().Message;
            item.State = CleanupItemState.Failed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Clean()
    {
        var selected = Items.Where(i => i.IsSelected && i.CanSelect).ToList();
        if (selected.Count == 0) return;

        long known = selected.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
        string list = string.Join("\n", selected.Take(12).Select(i => "• " + i.Title)) +
                      (selected.Count > 12 ? L.F("Cleanup.AndMore", selected.Count - 12) : "");
        string warnings = string.Join("\n", selected.Where(i => i.Warning is not null).Select(i => $"⚠ {i.Title}: {i.Warning}"));

        string message = L.F("Cleanup.ConfirmText", ByteSize.Format(known), list);
        if (warnings.Length > 0) message += "\n\n" + warnings;
        message += L.T("Cleanup.Irreversible");

        if (!await Dialogs.ConfirmAsync(L.T("Cleanup.ConfirmTitle"), message, L.T("Cleanup.ConfirmButton"), destructive: true))
            return;

        IsBusy = true;
        HasResult = false;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        long freed = 0;
        int skipped = 0, failed = 0;

        try
        {
            foreach (var item in selected)
            {
                ct.ThrowIfCancellationRequested();
                ProgressText = L.F("Cleanup.CleaningItem", item.Title);
                item.State = CleanupItemState.Cleaning;
                try
                {
                    var result = await item.Target.CleanAsync(ct);
                    freed += result.FreedBytes;
                    skipped += result.SkippedFiles;

                    item.StatusText = L.F("Cleanup.Freed", ByteSize.Format(result.FreedBytes)) +
                                      (result.SkippedFiles > 0 ? L.F("Cleanup.SkippedShort", result.SkippedFiles) : "") +
                                      (result.Note is null ? "" : $" · {result.Note}");

                    var after = await item.Target.AnalyzeAsync(ct);
                    item.Bytes = after.Bytes;
                    item.Files = after.Files;
                    item.State = CleanupItemState.Done;
                    item.IsSelected = false;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    item.StatusText = L.F("Cleanup.ErrorPrefix", ex.GetBaseException().Message);
                    item.State = CleanupItemState.Failed;
                }
            }

            ResultSeverity = failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            ResultMessage = L.F("Cleanup.ResultFreed", ByteSize.Format(freed)) +
                            (skipped > 0 ? L.F("Cleanup.ResultSkipped", skipped) : "") +
                            (failed > 0 ? L.F("Cleanup.ResultErrors", failed) : "");
        }
        catch (OperationCanceledException)
        {
            ResultSeverity = InfoBarSeverity.Informational;
            ResultMessage = L.F("Cleanup.Stopped", ByteSize.Format(freed));
            foreach (var item in selected.Where(i => i.State == CleanupItemState.Cleaning))
                item.State = CleanupItemState.Ready;
        }
        finally
        {
            HasResult = true;
            ProgressText = null;
            IsBusy = false;
            UpdateTotals();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void SelectRecommended()
    {
        foreach (var item in Items)
            item.IsSelected = item.CanSelect && item.Target.SelectedByDefault;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void SelectNone()
    {
        foreach (var item in Items) item.IsSelected = false;
    }

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();

    private void UpdateTotals()
    {
        var selected = Items.Where(i => i.IsSelected && i.CanSelect).ToList();
        long bytes = selected.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
        SelectedSummary = selected.Count == 0
            ? L.T("Cleanup.NothingSelected")
            : L.F("Cleanup.Selected", selected.Count, ByteSize.Format(bytes)) +
              (selected.Any(i => i.Bytes < 0) ? L.T("Cleanup.UnknownSize") : "");
        CleanCommand.NotifyCanExecuteChanged();
    }
}
