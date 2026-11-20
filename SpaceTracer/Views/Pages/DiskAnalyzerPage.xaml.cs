using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinTracer.ViewModels;

namespace WinTracer.Views.Pages;

public partial class DiskAnalyzerPage : Page
{
    private readonly DiskAnalyzerViewModel _viewModel;

    public DiskAnalyzerPage(DiskAnalyzerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNodeViewModel folder)
            _viewModel.SelectedFolder = folder;
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is TreeViewItem item)
            {
                item.Focus();
                item.IsSelected = true;
                return;
            }
            if (d is TreeView) return;
        }
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is TreeViewItem item)
        {
            item.BringIntoView();
            e.Handled = true;
        }
    }

    private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null and not DataGrid; d = GetParent(d))
        {
            if (d is DataGridRow { Item: FsItemViewModel item })
            {
                _viewModel.OpenCommand.Execute(item);
                e.Handled = true;
                return;
            }
        }
    }

    private static DependencyObject? GetParent(DependencyObject d) =>
        d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);
}
