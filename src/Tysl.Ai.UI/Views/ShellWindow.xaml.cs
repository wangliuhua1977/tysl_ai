using System.Windows;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views.Controls;

namespace Tysl.Ai.UI.Views;

public partial class ShellWindow : Window
{
    private SiteEditorDialog? editorDialog;
    private ShellViewModel? shellViewModel;

    public ShellWindow(AmapHostConfiguration mapHostConfiguration)
    {
        InitializeComponent();

        AmapHost.Configuration = mapHostConfiguration;
        AmapHost.PointSelected += HandleMapPointSelected;
        AmapHost.MapClicked += HandleMapClicked;
        AmapHost.RenderedPointsUpdated += HandleRenderedPointsUpdated;

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
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

    private void HandleMapPointSelected(object? sender, MapPointSelectedEventArgs e)
    {
        shellViewModel?.HandleMapPointSelected(e.DeviceCode);
    }

    private void HandleMapClicked(object? sender, MapClickedEventArgs e)
    {
        shellViewModel?.HandleMapClicked(e.Longitude, e.Latitude);
    }

    private void HandleRenderedPointsUpdated(object? sender, MapRenderedPointsEventArgs e)
    {
        shellViewModel?.HandleMapPointsRendered(e.Points);
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
        AmapHost.PointSelected -= HandleMapPointSelected;
        AmapHost.MapClicked -= HandleMapClicked;
        AmapHost.RenderedPointsUpdated -= HandleRenderedPointsUpdated;

        if (shellViewModel is not null)
        {
            shellViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
            shellViewModel.NotificationRequested -= HandleNotificationRequested;
            shellViewModel.Dispose();
        }
    }
}
