using System.Windows.Controls;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
