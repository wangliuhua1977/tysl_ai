using System.Windows;
using Tysl.Ai.Services.Dashboard;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views;

namespace Tysl.Ai.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dashboardService = new StubInspectionDashboardService();
        var shellViewModel = new ShellViewModel(dashboardService);
        var shellWindow = new ShellWindow
        {
            DataContext = shellViewModel
        };

        MainWindow = shellWindow;
        shellWindow.Show();
    }
}
