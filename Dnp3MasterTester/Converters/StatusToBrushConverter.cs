using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return Brushes.Gray;

        var text = value.ToString()?.ToLowerInvariant() ?? string.Empty;

        if (text.Contains("open"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

        if (text.Contains("closed") || text.Contains("on"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
