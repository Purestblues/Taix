using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Taix.Client.Controls.Base;

/// <summary>
/// 统一的「没有数据 / 正在加载」占位。
///
/// 之前各页各写一套：有的只有一行灰字、有的用写死的灰色插画，位置和文案都不一致。
/// 现在把两种状态收进同一个控件：<see cref="IsLoading"/> 为真时显示骨架条，
/// 否则显示插画 + 文案；文案留空时回落到 <c>NoData</c> 资源，跟随语言切换。
/// </summary>
public class EmptyData : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EmptyData, string?>(nameof(Text));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<EmptyData, string?>(nameof(Description));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<EmptyData, bool>(nameof(IsLoading));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<EmptyData, bool>(nameof(IsCompact));

    /// <summary>主文案，留空用默认的「暂时没有数据」</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>补充说明（可选），例如「换个时间范围试试」</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>加载中：显示骨架条而不是「没有数据」</summary>
    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>紧凑模式：不画插画，只留骨架/文案（用在卡片、窄区域里）</summary>
    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(EmptyData);
}
