using System.Windows.Controls;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class RegistryPage : Page
{
    public RegistryPage(RegistryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.EnsureScannedAsync();
    }
}
