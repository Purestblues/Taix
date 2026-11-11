using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Taix.Client.Behaviors;

/// <summary>
/// 让列表能用键盘进入：方向键选中之后按 Enter / Space，等同于「点这一行」。
/// 挂到 ListBox 上，命令参数自动取 SelectedItem（省掉每页各写一遍 KeyDown）。
/// </summary>
public static class KeyboardListActivation
{
    public static readonly AttachedProperty<ICommand?> EnterCommandProperty =
        AvaloniaProperty.RegisterAttached<SelectingItemsControl, ICommand?>(
            "EnterCommand", typeof(KeyboardListActivation));

    private static readonly AttachedProperty<bool> IsAttachedProperty =
        AvaloniaProperty.RegisterAttached<SelectingItemsControl, bool>(
            "IsAttached", typeof(KeyboardListActivation));

    public static ICommand? GetEnterCommand(SelectingItemsControl control) =>
        control.GetValue(EnterCommandProperty);

    public static void SetEnterCommand(SelectingItemsControl control, ICommand? value) =>
        control.SetValue(EnterCommandProperty, value);

    static KeyboardListActivation()
    {
        EnterCommandProperty.Changed.AddClassHandler<SelectingItemsControl>(OnEnterCommandChanged);
    }

    private static void OnEnterCommandChanged(SelectingItemsControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control.GetValue(IsAttachedProperty)) return;

        control.SetValue(IsAttachedProperty, true);
        control.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key is not (Key.Enter or Key.Space)) return;
        if (sender is not SelectingItemsControl control) return;

        var command = GetEnterCommand(control);
        var parameter = control.SelectedItem;

        if (command == null || parameter == null || !command.CanExecute(parameter)) return;

        command.Execute(parameter);
        e.Handled = true;
    }
}
