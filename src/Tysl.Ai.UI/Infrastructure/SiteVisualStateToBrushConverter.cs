using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.UI.Infrastructure;

public sealed class SiteVisualStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var suffix = parameter as string ?? "FillBrush";
        var visualState = value is SiteVisualState state ? state : SiteVisualState.Normal;
        var resourceKey = visualState switch
        {
            SiteVisualState.Fault => $"Status.Alert.{suffix}",
            SiteVisualState.Warning => $"Status.Warning.{suffix}",
            SiteVisualState.Idle => $"Status.Idle.{suffix}",
            SiteVisualState.Cooling => $"Status.Cooling.{suffix}",
            SiteVisualState.Dispatched => $"Status.Dispatched.{suffix}",
            SiteVisualState.Offline => $"Status.Offline.{suffix}",
            SiteVisualState.Unmonitored => $"Status.Unmonitored.{suffix}",
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
