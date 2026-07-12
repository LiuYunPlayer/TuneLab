using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Input;
using TuneLab.I18N;
using TuneLab.Utils;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding
using static TuneLab.GUI.Dialog;

namespace TuneLab.UI;

// 设置窗「快捷键」页：列出全部注册命令（按作用域 + Scripts 分组、可搜索），逐行录制手势 / 清除 / 重置默认。
// 显示手势与分发共用 Keymap 单一真相源；重绑即时落盘并广播 Changed（菜单标注随之刷新）。见 docs/keybinding-system.md §8、§9。
internal sealed class KeymapSettingsPage : DockPanel
{
    readonly Window mOwner;             // 弹窗宿主 + 翻译上下文（.Tr(mOwner) → "SettingsWindow" 键）
    readonly ListView mListView;
    string mSearch = string.Empty;
    string? mRecordingId;               // 正在录制手势的命令 id（null=未在录制）

    // 分组由命令 id 的功能域派生（纯显示，可随时调整；只有 id 域 key 本身是冻结契约）。顺序与标题在此定。
    // 域外/未列出的命令兜底归到各自域名（不隐藏）。见 docs/keybinding-system.md §1.1、§8。
    static readonly (string Domain, string Label)[] DomainOrder =
    {
        ("file", "File"), ("edit", "Edit"), ("transport", "Transport"),
        ("note", "Notes"), ("tool", "Tools"), ("part", "Part"),
        ("track", "Track"), ("view", "View"), ("app", "App"), ("script", "Scripts"),
    };

    // 命令 id 的顶级域：script:Foo → "script"；note.octaveUp → "note"；无点的 id 兜底归 "app"（安全网，约定上不该出现）。
    static string DomainOf(KeyCommand cmd)
    {
        if (cmd.Id.StartsWith("script:", StringComparison.Ordinal))
            return "script";
        int dot = cmd.Id.IndexOf('.');
        return dot > 0 ? cmd.Id[..dot] : "app";
    }

    public KeymapSettingsPage(Window owner)
    {
        mOwner = owner;

        var header = new DockPanel() { Margin = new(24, 16, 24, 8) };
        {
            var resetAll = MakeTextButton("Reset All".Tr(mOwner), async () =>
            {
                if (await Confirm("Reset All Shortcuts".Tr(mOwner), "Reset all shortcuts to their defaults?".Tr(mOwner)))
                {
                    Keymap.ResetAll();
                    Rebuild();
                }
            });
            header.AddDock(resetAll, Dock.Right);

            var search = new TextInput() { Height = 28, Watermark = "Search".Tr(mOwner), Margin = new(0, 0, 12, 0) };
            search.TextChanged.Subscribe(() => { mSearch = search.Text.Trim(); Rebuild(); });
            header.AddDock(search);
        }
        this.AddDock(header, Dock.Top);

        mListView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };
        // ScrollView 是 Panel，无背景则自身不可命中——滚轮只在命中的子控件（有文字处）上触发。
        // 透明背景让整个视口可命中，空白处也能滚动。
        mListView.Background = Brushes.Transparent;
        this.AddDock(mListView);

        // 录制期在页级隧道阶段抢先捕获按键：先于任一子控件（含 Button 的空格/回车激活、Window 的 Esc 关窗）。
        AddHandler(KeyDownEvent, OnRecordingKeyDown, RoutingStrategies.Tunnel);

        Rebuild();
    }

    void OnRecordingKeyDown(object? sender, KeyEventArgs e)
    {
        if (mRecordingId == null)
            return;

        // 纯修饰键：继续等待主键。
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var id = mRecordingId;
        mRecordingId = null;

        if (e.Key == Key.Escape)   // 取消录制（不关窗）
        {
            Rebuild();
            return;
        }

        _ = CommitGesture(id, new KeyBinding(e.Key, e.KeyModifiers & KeyBinding.ModifierMask));
    }

    async Task CommitGesture(string id, KeyBinding binding)
    {
        var conflictId = Keymap.FindConflict(id, binding);
        if (conflictId != null)
        {
            var otherName = Keymap.TryGet(conflictId, out var c) ? c.DisplayName() : conflictId;
            if (!await Confirm("Shortcut Conflict".Tr(mOwner),
                    string.Format("This shortcut is already used by \"{0}\". Reassign it?".Tr(mOwner), otherName)))
            {
                Rebuild();
                return;
            }
            Keymap.Rebind(conflictId, null);   // 解除原命令的绑定，手势归新命令
        }

        Keymap.Rebind(id, binding);
        Rebuild();
    }

    void Rebuild()
    {
        mListView.Content.Children.Clear();

        bool Match(KeyCommand cmd)
        {
            if (mSearch.Length == 0)
                return true;
            return cmd.DisplayName().Contains(mSearch, StringComparison.CurrentCultureIgnoreCase)
                || cmd.Id.Contains(mSearch, StringComparison.OrdinalIgnoreCase);
        }

        // 按功能域分组，组内按首次注册序排（反映逻辑顺序而非字母序）。
        var byDomain = Keymap.Commands.Where(Match)
            .GroupBy(DomainOf)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => Keymap.OrderOf(c.Id)).ToList());

        void EmitGroup(string domain, string label)
        {
            if (!byDomain.TryGetValue(domain, out var group))
                return;
            byDomain.Remove(domain);
            AddGroupHeader(label);
            foreach (var cmd in group)
                mListView.Content.Children.Add(BuildRow(cmd));
        }

        foreach (var (domain, label) in DomainOrder)
            EmitGroup(domain, label.Tr(mOwner));
        // 兜底：未在 DomainOrder 列出的域（如将来新增域忘了登记）按域名显示，不隐藏。
        foreach (var domain in byDomain.Keys.OrderBy(d => d).ToList())
            EmitGroup(domain, domain);

        if (mListView.Content.Children.Count == 0)
        {
            mListView.Content.Children.Add(new TextBlock
            {
                Text = "No matching commands.".Tr(mOwner),
                Margin = new Thickness(24, 16),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            });
        }
    }

    void AddGroupHeader(string text)
    {
        mListView.Content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(24, 18, 24, 2),
            Foreground = Style.LIGHT_WHITE.Opacity(0.85).ToBrush(),
        });
    }

    Control BuildRow(KeyCommand cmd)
    {
        var panel = new DockPanel() { Margin = new(36, 5, 24, 5) };

        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        // 重置（仅在有 override 时）：回默认手势。
        if (Keymap.HasOverride(cmd.Id))
            actions.Children.Add(MakeGlyphButton("↺", "Reset to Default".Tr(mOwner), () => { Keymap.ResetToDefault(cmd.Id); Rebuild(); }));

        // 清除（仅在当前有绑定时）：解绑。
        if (Keymap.Effective(cmd.Id) != null)
            actions.Children.Add(MakeGlyphButton("✕", "Clear".Tr(mOwner), () => { Keymap.Rebind(cmd.Id, null); Rebuild(); }));

        actions.Children.Add(MakeGestureChip(cmd));
        panel.AddDock(actions, Dock.Right);

        panel.AddDock(new TextBlock
        {
            Text = cmd.DisplayName(),
            FontSize = 14,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return panel;
    }

    // 手势芯片：显示当前手势（或「未绑定」），点击进入录制态。
    Border MakeGestureChip(KeyCommand cmd)
    {
        bool recording = mRecordingId == cmd.Id;
        var effective = Keymap.Effective(cmd.Id);
        var text = new TextBlock
        {
            Text = recording ? "Press keys…".Tr(mOwner) : (effective?.ToDisplayString() ?? "Unbound".Tr(mOwner)),
            FontSize = 12,
            Foreground = (effective == null && !recording ? Style.LIGHT_WHITE.Opacity(0.4) : Style.TEXT_LIGHT).ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var border = new Border
        {
            MinWidth = 96,
            Height = 26,
            Padding = new(10, 0),
            CornerRadius = new(4),
            Background = Style.BACK.ToBrush(),
            BorderThickness = new(1),
            BorderBrush = (recording ? Style.HIGH_LIGHT : Style.LIGHT_WHITE.Opacity(0.12)).ToBrush(),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = text,
        };
        border.PointerPressed += (s, e) =>
        {
            if (mRecordingId != null)
                return;
            mRecordingId = cmd.Id;
            text.Text = "Press keys…".Tr(mOwner);
            border.BorderBrush = Style.HIGH_LIGHT.ToBrush();
            border.Focus();   // 使随后按键沿隧道经本页，触发 OnRecordingKeyDown
        };
        // 失焦即视为取消（例如点了别处）：仅当仍在录制本行时复位。
        border.LostFocus += (s, e) =>
        {
            if (mRecordingId == cmd.Id)
            {
                mRecordingId = null;
                Rebuild();
            }
        };
        return border;
    }

    Border MakeTextButton(string text, Action onClick)
    {
        var border = new Border
        {
            Height = 28,
            Padding = new(12, 0),
            CornerRadius = new(4),
            Background = Style.BACK.ToBrush(),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        border.PointerPressed += (s, e) => onClick();
        return border;
    }

    Border MakeGlyphButton(string glyph, string tip, Action onClick)
    {
        var border = new Border
        {
            Width = 26,
            Height = 26,
            Margin = new(0, 0, 6, 0),
            CornerRadius = new(4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(border, tip);
        border.PointerPressed += (s, e) => onClick();
        return border;
    }

    Task<bool> Confirm(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var modal = new Dialog();
        modal.SetTitle(title);
        modal.SetMessage(message);
        modal.AddButton("Cancel".Tr(mOwner), ButtonType.Normal).Clicked += () => tcs.TrySetResult(false);
        modal.AddButton("OK".Tr(mOwner), ButtonType.Primary).Clicked += () => tcs.TrySetResult(true);
        modal.Topmost = true;
        _ = modal.ShowDialog(mOwner);
        return tcs.Task;
    }
}
