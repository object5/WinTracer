using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTracer.Core.Tweaks;
using WinTracer.Services;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly TweaksViewModel _owner;
    private bool _suppress;

    public TweakItemViewModel(Tweak tweak, TweaksViewModel owner)
    {
        Tweak = tweak;
        _owner = owner;
    }

    public Tweak Tweak { get; }
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string? Warning => Tweak.Warning;
    public string GroupTitle => TweakCatalog.GroupTitle(Tweak.Group);
    public int GroupOrder => (int)Tweak.Group;
    public bool CanToggle => !Tweak.RequiresAdmin || SystemShell.IsElevated;
    public bool NeedsAdmin => !CanToggle;

    public string? EffectText => Tweak.Effect switch
    {
        TweakEffect.RestartExplorer => L.T("Tweaks.Effect.Explorer"),
        TweakEffect.SignOut => L.T("Tweaks.Effect.SignOut"),
        TweakEffect.Reboot => L.T("Tweaks.Effect.Reboot"),
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial TweakState State { get; set; }

    public string? StateText => State switch
    {
        TweakState.Partial => L.T("Tweaks.State.Partial"),
        TweakState.Unknown => L.T("Tweaks.State.Unknown"),
        _ => null,
    };

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool IsWorking { get; set; }

    public async Task RefreshAsync()
    {
        var state = await Task.Run(Tweak.GetState);
        _suppress = true;
        State = state;
        IsOn = state == TweakState.Applied;
        _suppress = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_suppress) return;
        _ = ToggleAsync(value);
    }

    private async Task ToggleAsync(bool apply)
    {
        IsWorking = true;
        try
        {
            await Task.Run(apply ? Tweak.Apply : Tweak.Revert);
            await RefreshAsync();
            _owner.OnToggled(this, apply);
        }
        catch (Exception ex)
        {
            await RefreshAsync();
            _owner.ReportError(L.F("Tweaks.Error", Title, Describe(ex)));
        }
        finally
        {
            IsWorking = false;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException or System.Security.SecurityException => L.T("Common.NoAccess"),
        _ => ex.GetBaseException().Message,
    };
}

public sealed partial class BuiltInAppViewModel(BuiltInApp app, TweaksViewModel owner) : ObservableObject
{
    public BuiltInApp App { get; } = app;
    public string Title => App.Title;
    public string PackageName => App.PackageName;
    public string? Warning => App.Warning;

    [ObservableProperty]
    public partial bool IsWorking { get; set; }

    [RelayCommand]
    private Task Remove() => owner.RemoveAppAsync(this);
}

public sealed partial class TweaksViewModel : ObservableObject
{
    private bool _loadedOnce;

    public TweaksViewModel()
    {
        foreach (var tweak in TweakCatalog.Create())
            Tweaks.Add(new TweakItemViewModel(tweak, this));

        TweaksView = CollectionViewSource.GetDefaultView(Tweaks);
        TweaksView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakItemViewModel.GroupTitle)));
        TweaksView.SortDescriptions.Add(new SortDescription(nameof(TweakItemViewModel.GroupOrder), ListSortDirection.Ascending));
    }

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];
    public ICollectionView TweaksView { get; }
    public ObservableCollection<BuiltInAppViewModel> Apps { get; } = [];

    public bool IsElevated => SystemShell.IsElevated;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool AppsLoading { get; set; }

    [ObservableProperty]
    public partial bool NoAppsLeft { get; set; }

    [ObservableProperty]
    public partial bool ExplorerRestartSuggested { get; set; }

    [ObservableProperty]
    public partial bool RebootSuggested { get; set; }

    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity NoticeSeverity { get; set; }

    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await Refresh();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsLoading = true;
        try
        {
            await Task.WhenAll(Tweaks.Select(t => t.RefreshAsync()));
        }
        finally
        {
            IsLoading = false;
        }
        await LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        AppsLoading = true;
        try
        {
            var installed = await BuiltInApps.GetInstalledAsync();
            Apps.Clear();
            foreach (var app in BuiltInApps.Catalog.Where(a => installed.Contains(a.PackageName)))
                Apps.Add(new BuiltInAppViewModel(app, this));
            NoAppsLeft = Apps.Count == 0;
        }
        catch (Exception ex)
        {
            Show(L.F("Tweaks.AppsFailed", ex.GetBaseException().Message), InfoBarSeverity.Warning);
        }
        finally
        {
            AppsLoading = false;
        }
    }

    internal async Task RemoveAppAsync(BuiltInAppViewModel app)
    {
        string message = L.F("Tweaks.RemoveConfirm", app.Title, app.PackageName) +
                         (app.Warning is null ? "" : $"\n\n⚠ {app.Warning}") +
                         L.T("Tweaks.RemoveStore");
        if (!await Dialogs.ConfirmAsync(L.T("Tweaks.RemoveTitle"), message, L.T("Common.Delete"), destructive: true))
            return;

        app.IsWorking = true;
        try
        {
            await BuiltInApps.RemoveAsync(app.PackageName);
            Apps.Remove(app);
            NoAppsLeft = Apps.Count == 0;
            Show(L.F("Tweaks.Removed", app.Title), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Show(L.F("Tweaks.RemoveFailed", app.Title, ex.GetBaseException().Message), InfoBarSeverity.Error);
        }
        finally
        {
            app.IsWorking = false;
        }
    }

    internal void OnToggled(TweakItemViewModel item, bool applied)
    {
        if (item.Tweak.Effect == TweakEffect.RestartExplorer) ExplorerRestartSuggested = true;
        if (item.Tweak.Effect == TweakEffect.Reboot) RebootSuggested = true;
        Show(applied ? L.F("Tweaks.Applied", item.Title) : L.F("Tweaks.Reverted", item.Title), InfoBarSeverity.Success);
    }

    internal void ReportError(string message) => Show(message, InfoBarSeverity.Error);

    [RelayCommand]
    private void RestartExplorer()
    {
        SystemShell.RestartExplorer();
        ExplorerRestartSuggested = false;
    }

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();

    private void Show(string message, InfoBarSeverity severity)
    {
        NoticeMessage = message;
        NoticeSeverity = severity;
        HasNotice = true;
    }
}
