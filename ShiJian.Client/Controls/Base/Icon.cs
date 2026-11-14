using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using ShiJian.Client.Foundation.Rx;

namespace ShiJian.Client.Controls.Base;

public class Icon : TemplatedControl
{
    public static readonly StyledProperty<IconTypes> IconTypeProperty =
        AvaloniaProperty.Register<Icon, IconTypes>(nameof(IconType), IconTypes.Back);

    public static readonly DirectProperty<Icon, string> UnicodeProperty =
        AvaloniaProperty.RegisterDirect<Icon, string>(
            nameof(Unicode),
            o => o.Unicode,
            (o, v) => o.Unicode = v);

    private string _unicode = IconConverter.ToUnicode(IconTypes.Back);

    static Icon()
    {
        IconTypeProperty.Changed.AddClassHandler<Icon>((o, _) => o.OnIconTypeChanged(o.IconType));
    }

    public IconTypes IconType
    {
        get => GetValue(IconTypeProperty);
        set => SetValue(IconTypeProperty, value);
    }

    public string Unicode
    {
        get => _unicode;
        set => SetAndRaise(UnicodeProperty, ref _unicode, value);
    }

    private void OnIconTypeChanged(IconTypes icon)
    {
        Unicode = IconConverter.ToUnicode(icon);
    }
}