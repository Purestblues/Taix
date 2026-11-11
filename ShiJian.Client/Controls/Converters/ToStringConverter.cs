using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ShiJian.Client.Controls.Converters;

public class ToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}