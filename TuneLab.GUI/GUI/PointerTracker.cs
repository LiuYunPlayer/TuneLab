using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace TuneLab.GUI;

/// <summary>
/// 每个窗口最后一次已知的指针位置（窗口坐标）。用于在没有指针事件时回答"指针现在是否停在某控件上"——
/// 控件刚出现在静止的指针下（面板重建）时，Avalonia 对它的命中检测要等后续画面更新、时有时无，不能指望。
/// </summary>
/// <remarks>
/// 在窗口上以隧道路由、含已处理事件的方式旁听指针移动 / 进出：不影响任何人的事件处理。指针离开窗口即记为无。
/// 只在被 Watch 过的窗口上记录（首个需要它的控件进入可视树时调用）。仅限 UI 线程使用。
/// </remarks>
internal static class PointerTracker
{
    /// <summary>开始记录 visual 所在窗口的指针位置（幂等）。</summary>
    public static void Watch(Visual visual)
    {
        if (TopLevel.GetTopLevel(visual) is not { } top || sStates.TryGetValue(top, out _))
            return;

        var state = new State();
        sStates.Add(top, state);
        top.AddHandler(InputElement.PointerMovedEvent, (_, e) => state.Update(e, top), RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerEnteredEvent, (_, e) => state.Update(e, top), RoutingStrategies.Tunnel | RoutingStrategies.Direct, handledEventsToo: true);
        top.AddHandler(InputElement.PointerExitedEvent, (_, e) =>
        {
            if (e.Source == top)
                state.Position = null;   // 离开窗口
        }, RoutingStrategies.Tunnel | RoutingStrategies.Direct, handledEventsToo: true);
    }

    /// <summary>
    /// 指针此刻是否停在 visual 上（按最后已知位置）；是则给出 visual 内的坐标与修饰键。
    /// 按布局几何判：落在 visual 范围内、且落在每一层裁剪祖先（如侧栏滚动区的可视窗口）之内。不用 InputHitTest——
    /// 它查的是合成器的场景，渲染提交后才异步更新，刚出现的控件这一拍还不在里面。不处理同层兄弟间的遮挡（属性面板里没有）。
    /// </summary>
    public static bool TryGetOver(Visual visual, out Point position, out KeyModifiers modifiers)
    {
        position = default;
        modifiers = KeyModifiers.None;
        if (TopLevel.GetTopLevel(visual) is not { } top || !sStates.TryGetValue(top, out var state) || state.Position is not { } p)
            return false;
        if (!visual.IsEffectivelyVisible || !Contains(top, p, visual, out var local))
            return false;
        for (var ancestor = visual.GetVisualParent(); ancestor != null && ancestor != top; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor.ClipToBounds && !Contains(top, p, ancestor, out _))
                return false;
        }
        position = local;
        modifiers = state.Modifiers;
        return true;
    }

    static bool Contains(TopLevel top, Point point, Visual visual, out Point local)
    {
        local = default;
        if (top.TranslatePoint(point, visual) is not { } p)
            return false;
        local = p;
        return new Rect(visual.Bounds.Size).Contains(p);
    }

    sealed class State
    {
        public Point? Position;
        public KeyModifiers Modifiers;

        public void Update(PointerEventArgs e, TopLevel top)
        {
            Position = e.GetPosition(top);
            Modifiers = e.KeyModifiers;
        }
    }

    static readonly ConditionalWeakTable<TopLevel, State> sStates = new();
}
