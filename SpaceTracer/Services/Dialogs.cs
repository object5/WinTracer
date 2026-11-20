using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;
using WinTracer.Localization;

namespace WinTracer.Services;

public static class Dialogs
{
    public static IntPtr MainWindowHandle =>
        Application.Current.MainWindow is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = confirmText,
            PrimaryButtonAppearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary,
            CloseButtonText = L.T("Common.Cancel"),
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }
}
