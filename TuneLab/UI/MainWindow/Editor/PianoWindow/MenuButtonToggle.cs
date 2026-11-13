using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace TuneLab.UI;

// 点一下弹菜单、菜单开着时再点一下收起（不重新弹）的按钮。
// 难点：按下按钮的那一下，菜单可能已经被当成"点到外面"关掉了，松开时按钮又把它弹出来，看上去就关不掉。
// 所以按下时就记下菜单是不是开着（或刚被这一下关掉），点击时据此只收不弹。
internal static class MenuButtonToggle
{
    // open：弹出菜单并返回它；返回 null = 这次没有可弹的。
    public static void Attach(TuneLab.GUI.Components.Button button, Func<ContextMenu?> open)
    {
        ContextMenu? menu = null;
        long closedAt = long.MinValue / 2;
        bool closeOnClick = false;
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            closeOnClick = menu != null || Environment.TickCount64 - closedAt < ReopenGuardMs,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        button.Clicked += () =>
        {
            if (closeOnClick)
            {
                menu?.Close();
                return;
            }
            if (open() is not { } opened)
                return;
            menu = opened;
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(menu, opened))
                    menu = null;
                closedAt = Environment.TickCount64;
            };
        };
    }

    const long ReopenGuardMs = 300;
}
