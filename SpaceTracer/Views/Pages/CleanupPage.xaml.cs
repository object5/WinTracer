using System.Windows.Controls;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class CleanupPage : Page
{
    public CleanupPage(CleanupViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await viewModel.EnsureAnalyzedAsync();
    }
}
