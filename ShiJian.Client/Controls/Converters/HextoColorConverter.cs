using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ShiJian.Client.Base.Color;

namespace ShiJian.Client.Controls.Converters;

public class HextoColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;
        var colorStr = value.ToString()!;
        var opacity = 1.0;
        if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var op))
            opacity = op;
        return global::ShiJian.Client.Base.Color.Colors.GetFromString(colorStr, opacity);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}