using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ShiJian.Client.Controls.Converters;

/// <summary>
/// 布尔 → 网格列宽。用在详细页：应用维度时表盘占 <c>*</c>、榜单固定宽度；
/// 网站维度没有表盘，榜单要吃掉整行，所以两列的宽度都得跟着换。
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public string TrueValue { get; set; } = "*";

    public string FalseValue { get; set; } = "*";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => GridLength.Parse(value is bool b && b ? TrueValue : FalseValue);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
