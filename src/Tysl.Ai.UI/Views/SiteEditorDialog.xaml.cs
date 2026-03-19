using System.Windows;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views;

public partial class SiteEditorDialog : Window
{
    private SiteEditorViewModel? viewModel;

    public SiteEditorDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SiteEditorViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= HandleCloseRequested;
        }

        viewModel = e.NewValue as SiteEditorViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.CloseRequested += HandleCloseRequested;
    }

    private void HandleCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
