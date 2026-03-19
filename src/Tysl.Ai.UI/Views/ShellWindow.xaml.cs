using System.Windows;
using System.Windows.Input;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views;

public partial class ShellWindow : Window
{
    private SiteEditorDialog? editorDialog;
    private ShellViewModel? shellViewModel;

    public ShellWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void MapSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (shellViewModel is null || MapClickSurface.ActualWidth <= 0 || MapClickSurface.ActualHeight <= 0)
        {
            return;
        }

        var position = e.GetPosition(MapClickSurface);
        var relativeX = position.X / MapClickSurface.ActualWidth;
        var relativeY = position.Y / MapClickSurface.ActualHeight;
        shellViewModel.HandleMapSurfaceClick(relativeX, relativeY);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel oldViewModel)
        {
            oldViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
            oldViewModel.NotificationRequested -= HandleNotificationRequested;
        }

        shellViewModel = e.NewValue as ShellViewModel;
        if (shellViewModel is null)
        {
            return;
        }

        shellViewModel.EditorDialogRequested += HandleEditorDialogRequested;
        shellViewModel.NotificationRequested += HandleNotificationRequested;
    }

    private void HandleEditorDialogRequested(object? sender, SiteEditorDialogRequestedEventArgs e)
    {
        if (editorDialog is not null)
        {
            editorDialog.Close();
        }

        editorDialog = new SiteEditorDialog
        {
            Owner = this,
            DataContext = e.ViewModel
        };
        editorDialog.Closed += HandleEditorDialogClosed;
        editorDialog.Show();
        editorDialog.Activate();
    }

    private void HandleEditorDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not SiteEditorDialog dialog)
        {
            return;
        }

        dialog.Closed -= HandleEditorDialogClosed;

        if (dialog.DataContext is SiteEditorViewModel viewModel)
        {
            shellViewModel?.HandleEditorClosed(viewModel);
        }

        if (ReferenceEquals(editorDialog, dialog))
        {
            editorDialog = null;
        }
    }

    private void HandleNotificationRequested(object? sender, NotificationRequestedEventArgs e)
    {
        MessageBox.Show(this, e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (shellViewModel is not null)
        {
            shellViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
            shellViewModel.NotificationRequested -= HandleNotificationRequested;
        }
    }
}
