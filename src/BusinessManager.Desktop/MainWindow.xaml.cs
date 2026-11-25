using System.ComponentModel;
using System.Windows;
using BusinessManager.Desktop.ViewModels;

namespace BusinessManager.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.HasUnsavedWork &&
            MessageBox.Show(
                this,
                "A sale is in progress and has not been completed. Close anyway?",
                "Business Manager",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
