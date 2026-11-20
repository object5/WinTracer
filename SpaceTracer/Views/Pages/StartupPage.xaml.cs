using System.Windows.Controls;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class StartupPage : Page
{
    public StartupPage(StartupViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.EnsureLoadedAsync();
    }
}
