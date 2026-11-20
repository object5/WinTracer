using System.Windows.Controls;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class TweaksPage : Page
{
    public TweaksPage(TweaksViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.EnsureLoadedAsync();
    }
}
