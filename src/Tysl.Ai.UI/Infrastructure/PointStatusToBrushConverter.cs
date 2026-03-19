using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.UI.Infrastructure;

public sealed class PointStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var suffix = parameter as string ?? "FillBrush";
        var status = value is PointStatus pointStatus ? pointStatus : PointStatus.Normal;
        var resourceKey = status switch
        {
            PointStatus.Monitoring => $"Status.Monitoring.{suffix}",
            PointStatus.Normal => $"Status.Normal.{suffix}",
            PointStatus.Alert => $"Status.Alert.{suffix}",
            PointStatus.Dispatched => $"Status.Dispatched.{suffix}",
            PointStatus.Offline => $"Status.Offline.{suffix}",
            _ => $"Status.Normal.{suffix}"
        };

        return Application.Current.TryFindResource(resourceKey) as Brush
            ?? Application.Current.TryFindResource("AccentBrush") as Brush
            ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
