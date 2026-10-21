using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LinkMend.Core.Models;

namespace LinkMend.App.Converters;

public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ShortcutHealth health
            ? health switch
            {
                ShortcutHealth.Healthy => Brush("#0F9F6E"),
                ShortcutHealth.NeedsAttention => Brush("#C47B08"),
                ShortcutHealth.Broken => Brush("#D64545"),
                ShortcutHealth.Unsupported => Brush("#7C5CFC"),
                _ => Brush("#7C8799")
            }
            : Brush("#7C8799");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class HealthToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ShortcutHealth health
            ? health switch
            {
                ShortcutHealth.Healthy => Brush("#E7F7F1"),
                ShortcutHealth.NeedsAttention => Brush("#FFF5E3"),
                ShortcutHealth.Broken => Brush("#FDECEC"),
                ShortcutHealth.Unsupported => Brush("#F1EDFF"),
                _ => Brush("#EEF1F5")
            }
            : Brush("#EEF1F5");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class HealthToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ShortcutHealth health
            ? health switch
            {
                ShortcutHealth.Healthy => "正常",
                ShortcutHealth.NeedsAttention => "需注意",
                ShortcutHealth.Broken => "已失效",
                ShortcutHealth.Unsupported => "特殊项",
                ShortcutHealth.Unreadable => "无法读取",
                _ => "未知"
            }
            : "未知";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IssueSeverity severity
            ? severity switch
            {
                IssueSeverity.Error => Brush("#D64545"),
                IssueSeverity.Warning => Brush("#C47B08"),
                _ => Brush("#2F6BFF")
            }
            : Brush("#7C8799");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null;
        var visible = Invert ? !isNull : isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && !flag ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
