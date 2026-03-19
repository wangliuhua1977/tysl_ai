using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Dashboard;

public interface IInspectionDashboardService
{
    DashboardSnapshot GetSnapshot();
}
