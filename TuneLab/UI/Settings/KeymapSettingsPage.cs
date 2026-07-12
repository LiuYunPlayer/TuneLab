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

    static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x5C));    // 同域冲突（红，须消解）
    static readonly IBrush ShareBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xC0, 0x4E));   // 跨域共用（黄，提示）

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

        // 跨域同手势不算冲突（焦点解析：聚焦哪个子树，哪层绑定生效、内层遮蔽外层，见 docs/keybinding-system.md §9）。
        // 不阻止，但绑定后告知，免得用户以为其中某个"失灵"。
        var others = OtherScopeUsers(id, binding);
        if (others.Count > 0)
        {
            var names = string.Join(", ", others.Select(c => "\"" + c.DisplayName() + "\""));
            await Inform("Shortcut Shared Across Areas".Tr(mOwner),
                string.Format("This shortcut is also used by {0} in another area. Both stay active — the binding for the focused area takes priority.".Tr(mOwner), names));
        }
    }

    // 同手势但不同作用域的其它命令（跨域共用，非冲突）。self 须已注册（设置页显示的即注册命令）。
    static IReadOnlyList<KeyCommand> OtherScopeUsers(string id, KeyBinding binding)
    {
        if (!Keymap.TryGet(id, out var self))
            return Array.Empty<KeyCommand>();
        var list = new List<KeyCommand>();
        foreach (var cmd in Keymap.Commands)
        {
            if (cmd.Id == id || cmd.Scope == self.Scope)
                continue;
            if (Keymap.Effective(cmd.Id) is { } g && g.Equals(binding))
                list.Add(cmd);
        }
        return list;
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

        // 操作区固定列布局：重置(↺) | 清除(✕) | 手势芯片。各列定宽、条件图标缺失留空位、芯片也定宽——跨行对齐成列。
        // 冲突指示（⚠）不占独立列，而是嵌进芯片、置于手势文字前（见 MakeGestureChip）；罕见的 ↺ 置最左，其空位并入
        // "命令名↔动作簇"留白，使常态下 ✕ 与芯片始终紧贴、无多余间隔。
        var actions = new Grid { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        actions.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(30)));   // ↺ 重置
        actions.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(30)));   // ✕ 清除
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));      // 手势芯片（定宽）

        void PlaceCol(Control c, int col) { Grid.SetColumn(c, col); actions.Children.Add(c); }

        // 重置（仅在有 override 时）：回默认手势。
        if (Keymap.HasOverride(cmd.Id))
            PlaceCol(MakeGlyphButton("↺", "Reset to Default".Tr(mOwner), () => { Keymap.ResetToDefault(cmd.Id); Rebuild(); }), 0);

        // 清除（仅在当前有绑定时）：解绑。
        var effective = Keymap.Effective(cmd.Id);
        if (effective != null)
            PlaceCol(MakeGlyphButton("✕", "Clear".Tr(mOwner), () => { Keymap.Rebind(cmd.Id, null); Rebuild(); }), 1);

        // 持久冲突展示（不止绑定当时），用彩色 ⚠（嵌芯片、文字前）+ 同色手势文字编码，原因挂芯片 tooltip：
        // 同域撞键=红（须消解，来自手改 JSON / 多脚本同默认等）；跨域共用=黄（焦点共存、非错误）。见 §9。
        IBrush? chipColor = null;
        string? chipTip = null;
        if (effective != null)
        {
            var peers = Keymap.SameScopeConflictPeers(cmd.Id);
            if (peers.Count > 0)
            {
                // 冲突双方【都】警示（用户对各方有同等修改权，应综合判断而非被诱导只改败者）。稳定决胜者=同组注册序
                // 最小者（分发生效者），tooltip 各自点明"当前谁生效"，把完整信息交用户权衡。见 docs/keybinding-system.md §9。
                string winner = cmd.Id;
                foreach (var p in peers)
                    if (Keymap.OrderOf(p) < Keymap.OrderOf(winner)) winner = p;
                var peerNames = string.Join("、", peers.Select(NameOf));
                chipColor = WarnBrush;
                chipTip = winner == cmd.Id
                    ? string.Format("Conflicts with {0} in the same area; this one currently takes effect.".Tr(mOwner), peerNames)
                    : string.Format("Conflicts with {0} in the same area; \"{1}\" currently takes effect.".Tr(mOwner), peerNames, NameOf(winner));
            }
            else
            {
                var others = OtherScopeUsers(cmd.Id, effective.Value);
                if (others.Count > 0)
                {
                    chipColor = ShareBrush;
                    chipTip = string.Format("Also bound to {0} in another area; the focused area wins.".Tr(mOwner),
                        string.Join("、", others.Select(c => c.DisplayName())));
                }
            }
        }

        PlaceCol(MakeGestureChip(cmd, chipColor, chipTip), 2);
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

    // 手势芯片：显示当前手势（或「未绑定」），点击进入录制态。conflictBorder!=null 时描该色边（红=同域冲突/
    // 黄=跨域共用），并挂 tip 说明；录制态恒以高亮边覆盖。冲突用手势【文字】上色（比描边更醒目、直指冲突的绑定本身）。
    Border MakeGestureChip(KeyCommand cmd, IBrush? conflictColor = null, string? tip = null)
    {
        bool recording = mRecordingId == cmd.Id;
        var effective = Keymap.Effective(cmd.Id);
        var normalFore = (effective == null && !recording ? Style.LIGHT_WHITE.Opacity(0.4) : Style.TEXT_LIGHT).ToBrush();
        // 有冲突且非录制、且当前确有手势时 → ⚠ 前缀 + 文字整体染冲突色（红/黄）；否则常规色。
        bool showConflict = conflictColor != null && !recording && effective != null;
        var gesture = recording ? "Press keys…".Tr(mOwner) : (effective?.ToDisplayString() ?? "Unbound".Tr(mOwner));
        var text = new TextBlock
        {
            Text = showConflict ? "⚠ " + gesture : gesture,
            FontSize = 12,
            Foreground = showConflict ? conflictColor : normalFore,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,   // 极长组合键兜底，不撑破定宽
        };
        var border = new Border
        {
            Width = 124,   // 定宽：跨行芯片左缘对齐，不随手势文本长短漂移；略宽以容纳 ⚠ 前缀
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
        if (tip != null)
            ToolTip.SetTip(border, tip);
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

    // 命令 id → 显示名（未注册则回退 id）。
    static string NameOf(string id) => Keymap.TryGet(id, out var c) ? c.DisplayName() : id;

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

    // 单按钮告知（不阻止，仅解释），用于跨域共用手势的焦点遮蔽提示。
    Task Inform(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var modal = new Dialog();
        modal.SetTitle(title);
        modal.SetMessage(message);
        modal.AddButton("OK".Tr(mOwner), ButtonType.Primary).Clicked += () => tcs.TrySetResult(true);
        modal.Topmost = true;
        _ = modal.ShowDialog(mOwner);
        return tcs.Task;
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
