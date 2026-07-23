using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.I18N;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using IEffect = TuneLab.Data.IEffect;

namespace TuneLab.UI;

// 效果链管理面板：列出目标 MidiPart 的效果器链，支持增/删/替换/重排/bypass，并复用 PropertyObjectController
// 渲染每个 effect 的参数。时间轴自动化编辑（per-effect 自动化曲线）本版未做，参数以 Properties 面板为准。
//
// 多 part 合并语义（参照容器多选「数组家族按 index 对齐」判例，不因链不齐整块隐藏）：槽位数 = 最长链，
// 各 part 逐槽位对齐；短链 part 该槽位视作 empty 占位。占位两种形态：UI 幻影（该 part 链尾之外的槽，
// 无数据）与**数据级空 effect**（Type 空 → 引擎缺失天然 passthrough，Add 补齐对齐时落盘，显示 (empty)）。
//   · 槽内非空实例 Type 全等 → 完全合并：bypass/参数三态（MultipleDataProperty/MultipleDataPropertyObject，
//     只绑有实例的 part）、config 经多实例 context 由引擎合并（Effect.GetPropertyConfig(slot)）、
//     自动化默认值行（缺位 part 天然按默认值参与三态、写扇出时自动跳过）；
//   · 类型不等 → 受限行：标题 "(Multiple)"、保留 bypass 三态与替换/删除，参数区不展示。
// 「替换」（类型标签即按钮，单选也可用）：有实例的 part 原位换新类型（同槽位，颤音关联条目按孤儿语义保留），
// 链长恰为该槽位的 part 补位；更短的 part 跳过（先补前面的空槽）。移位仅在各 part 链等长时提供
//（不等长下槽位移动语义含混；拖拽重排 + 挤压动画是在案的后续形态）。
// 结构操作扇出全部 part、共享文档一次 Commit 归一个撤销单元。单 part 即 N=1 特例。
internal class EffectsController : StackPanel
{
    public EffectsController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    public void SetParts(IReadOnlyList<IMidiPart> parts)
    {
        s.DisposeAll();
        mParts = parts;

        foreach (var part in mParts)
        {
            // 链结构变化（增删/替换/重排）整槽位重建；每个 effect 的参数/启用经逐字段绑定自动刷新与提交。
            // 本面板自己的扇出操作批量期间抑制（见 WithBatch），批完只重建一次。
            part.Effects.MembershipModified.Subscribe(OnMembershipModified, s);
        }

        Rebuild();
    }

    void OnMembershipModified()
    {
        if (!mBatching)
            Rebuild();
    }

    void Rebuild()
    {
        foreach (var view in mEffectViews)
            view.Dispose();
        mEffectViews.Clear();
        Children.Clear();

        if (mParts.Count == 0)
            return;

        int slotCount = 0;
        bool sameLength = true;
        foreach (var part in mParts)
        {
            if (part.Effects.Count != mParts[0].Effects.Count)
                sameLength = false;
            slotCount = Math.Max(slotCount, part.Effects.Count);
        }

        for (int i = 0; i < slotCount; i++)
        {
            int index = i;
            var slot = mParts.Select(part => index < part.Effects.Count ? part.Effects[index] : null).ToList();
            var view = new EffectView(this, mParts, slot, i, canMove: sameLength);
            mEffectViews.Add(view);
            Children.Add(view.Root);
        }

        mAddButton = MakeTextButton("+ " + "Add Effect".Tr(TC.Property), 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        Children.Add(mAddButton);

        // Add Effect 按钮下方收尾分隔线（与各 effect 块、面板其他区块的分界一致）。
        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
    }

    // 引擎选择菜单（Add 与替换共用）；无引擎时列一条不可用占位。
    ContextMenu BuildEngineMenu(Action<string> onPick)
    {
        var menu = new ContextMenu();
        var engines = EffectManager.GetAllEffectEngines();
        if (engines.Count == 0)
        {
            menu.Items.Add(new MenuItem().SetName("No effect installed".Tr(TC.Property)).SetAction(() => { }));
            return menu;
        }
        foreach (var type in engines)
        {
            var captured = type;
            menu.Items.Add(new MenuItem().SetName(EffectManager.GetDisplayName(captured)).SetAction(() => onPick(captured)));
        }
        return menu;
    }

    void OnAddButtonClicked()
    {
        if (mParts.Count == 0)
            return;
        mAddButton?.OpenContextMenu(BuildEngineMenu(AddEffect));
    }

    // 结构操作统一形状：扇出到所有 part、共享文档一次 Commit、批完重建一次。
    void WithBatch(Action mutate)
    {
        if (mParts.Count == 0)
            return;

        mBatching = true;
        try
        {
            mutate();
            mParts[0].Commit("Edit Effects");   // 全 part 共享同一文档撤销栈：一次提交归一个撤销单元
        }
        finally
        {
            mBatching = false;
        }
        Rebuild();
    }

    // 追加新效果：多选链不等长时先给短链 part 补**数据级空占位**（Type 空 → 引擎缺失天然 passthrough、
    // 序列化/撤销全兼容，UI 显示 (empty) 可随时替换成真 effect），使新效果在所有 part 落进同一槽位、
    // 补完后各链等长（移位随之可用）。
    void AddEffect(string type)
    {
        WithBatch(() =>
        {
            int slotCount = 0;
            foreach (var part in mParts)
                slotCount = Math.Max(slotCount, part.Effects.Count);

            foreach (var part in mParts)
            {
                while (part.Effects.Count < slotCount)
                    part.InsertEffect(part.Effects.Count, part.CreateEffect(new() { Type = string.Empty }));
                part.InsertEffect(part.Effects.Count, part.CreateEffect(new() { Type = type }));
            }
        });
    }

    // 删除槽位：只作用于有该槽位的 part。颤音影响表按实例 id 锚定：条目成孤儿保留（不裁剪），undo 同 id 重连。
    void RemoveEffect(int index)
    {
        WithBatch(() =>
        {
            foreach (var part in mParts)
            {
                if (index >= part.Effects.Count)
                    continue;
                part.RemoveEffect(part.Effects[index]);
            }
        });
    }

    // 替换槽位为指定类型：**保留全部用户数据、只换 Type**（GetInfo → 换 Type → 原位重建）——id、参数、
    // 自动化曲线、分段轨、bypass、颤音关联全保留；新引擎不认识的键/轨按孤儿语义隐藏保留，换回原类型全量恢复
    //（与 voice 换引擎不清 automation 同判例）。链长恰为该槽位的 part 补位（empty 只在链尾连续，补位即落在本槽、
    // 新发 id）；更短的 part 跳过（先补前面的空槽）。
    void ReplaceEffect(int index, string type)
    {
        WithBatch(() =>
        {
            foreach (var part in mParts)
            {
                if (index < part.Effects.Count)
                {
                    var old = part.Effects[index];
                    if (old.Type == type)
                        continue;
                    var info = old.GetInfo();
                    info.Type = type;
                    part.RemoveEffect(old);
                    part.InsertEffect(index, part.CreateEffect(info));
                }
                else if (index == part.Effects.Count)
                {
                    part.InsertEffect(index, part.CreateEffect(new() { Type = type }));
                }
            }
        });
    }

    // 移位（仅链等长时可用，Rebuild 已门控按钮显隐）；颤音关联按实例 id 锚定，随实例走、无需任何簿记。
    void MoveEffect(int index, int delta)
    {
        if (mParts.Count == 0)
            return;

        int target = index + delta;
        if (target < 0 || target >= mParts[0].Effects.Count)
            return;

        WithBatch(() =>
        {
            foreach (var part in mParts)
            {
                if (index >= part.Effects.Count || target >= part.Effects.Count)
                    continue;
                var effect = part.Effects[index];
                part.RemoveEffect(effect);
                part.InsertEffect(target, effect);
            }
        });
    }

    static Button MakeTextButton(string text, double width)
    {
        var button = new Button();
        if (width > 0)
            button.Width = width;
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } });
        button.AddContent(new() { Item = new TextItem() { Text = text, FontSize = 12 }, ColorSet = new() { Color = Colors.White } });
        return button;
    }

    // 一个「槽位」的视图（多选时 = 各 part 同槽位实例的合并面，slot 与 parts 按下标对齐、元素可为 null=empty）：
    // 标题行（bypass/类型即替换按钮/上移/下移/删除）+ 类型全等时的参数 PropertyObjectController +
    // 该槽位连续自动化轨的默认值行（直接混在参数后，不另立标题——effect 块本身已有类型表头）。
    class EffectView
    {
        public Control Root => mRoot;

        public EffectView(EffectsController owner, IReadOnlyList<IMidiPart> parts, IReadOnlyList<IEffect?> slot, int index, bool canMove)
        {
            mIndex = index;

            // 非空实例及其所属 part（操作与绑定的实际作用集；empty 槽位的 part 不参与）。
            for (int j = 0; j < slot.Count; j++)
            {
                if (slot[j] is IEffect effect)
                {
                    mSlotEffects.Add(effect);
                    mSlotParts.Add(parts[j]);
                }
            }
            bool typesEqual = mSlotEffects.All(effect => effect.Type == mSlotEffects[0].Type);

            // bypass：单实例直绑；多实例合并成一个三态叶子（全等显该值、不等 dash），写扇出 + 共享撤销根。
            var bypass = new CheckBox() { Margin = new(24, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            bypass.BindDataProperty(
                mSlotEffects.Count == 1 ? mSlotEffects[0].IsEnabled : new MultipleDataProperty<bool>(mSlotEffects.Select(effect => effect.IsEnabled).ToList(), false, PropertyValue.Create), s);

            // 类型标签即「替换」入口：点击弹引擎菜单，选择即把本槽位换成该类型（类型不等的槽位显示 (Multiple)、
            // 空占位显示 (empty)，替换正是把它统一/填实的操作）。用 Label 而非自绘 Button——后者不按文本自适应宽度；
            // 透明底让整行空白区参与命中测试。
            string title = !typesEqual ? "(Multiple)"
                : string.IsNullOrEmpty(mSlotEffects[0].Type) ? "(empty)"
                : EffectManager.GetDisplayName(mSlotEffects[0].Type);
            var typeLabel = new Label()
            {
                Content = title,
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                Background = Avalonia.Media.Brushes.Transparent,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            typeLabel.PointerPressed += (_, e) =>
            {
                typeLabel.OpenContextMenu(owner.BuildEngineMenu(type => owner.ReplaceEffect(mIndex, type)));
                e.Handled = true;
            };

            var buttons = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 0, 24, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            if (canMove)
            {
                var up = MakeTextButton("↑", 28); up.Height = 28; up.Clicked += () => owner.MoveEffect(mIndex, -1);
                var down = MakeTextButton("↓", 28); down.Height = 28; down.Clicked += () => owner.MoveEffect(mIndex, 1);
                buttons.Children.Add(up);
                buttons.Children.Add(down);
            }
            var remove = MakeTextButton("✕", 28); remove.Height = 28; remove.Clicked += () => owner.RemoveEffect(mIndex);
            buttons.Children.Add(remove);

            // 标题行底色用 INTERFACE，与侧栏其他控件块一致（不再用更深的 BACK）。
            var header = new DockPanel() { Height = 38, Background = Style.INTERFACE.ToBrush(), LastChildFill = true };
            DockPanel.SetDock(bypass, Dock.Left);
            DockPanel.SetDock(buttons, Dock.Right);
            header.Children.Add(bypass);
            header.Children.Add(buttons);
            header.Children.Add(typeLabel);

            mRoot = new StackPanel() { Orientation = Orientation.Vertical };
            mRoot.Children.Add(header);
            // 分界线放在标题行【下方】（标题行与参数之间）：既给出标题↔参数的分界，又避免它叠到上一块底部 / 面板分隔线上造成双线。
            mRoot.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

            // 参数面板与自动化默认值行仅在槽内类型全等时展示（引擎不同无从合并 config）；
            // 部分 part 缺位不妨碍——只绑有实例的成员，默认值行对缺位 part 天然按默认值参与、写扇出自动跳过。
            if (typesEqual)
            {
                // 参数面板：逐字段绑定到（合并的）effect.Properties，值的下发/写回/撤销刷新全自动；多实例走三态合并数据源。
                // 条件面板：参数 commit 后按当前值重算整棵 config 并 keyed-diff 到控件树；多实例 config 经多实例 context 由引擎合并。
                mController = new PropertyObjectController();
                mController.SetConfig(SlotPropertyConfig(), SlotData());
                foreach (var effect in mSlotEffects)
                    effect.Properties.Modified.Subscribe(ReconcileController, s);

                // 自动化默认值外部（undo/redo/preset）改动 → 刷新所有行（结构不变，无需重建）。
                foreach (var effect in mSlotEffects)
                    effect.Automations.WhenAny(automation => automation.DefaultValue.Modified).Subscribe(RefreshAutomationRows, s);

                mRoot.Children.Add(mController);
                mRoot.Children.Add(mAutomationContainer);
                RebuildAutomationRows();
            }
        }

        ObjectConfig SlotPropertyConfig()
            => mSlotEffects.Count == 1 ? mSlotEffects[0].PropertyConfig : Data.Effect.GetPropertyConfig(mSlotEffects);

        IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SlotAutomationConfigs()
            => mSlotEffects.Count == 1 ? mSlotEffects[0].AutomationConfigs : Data.Effect.GetAutomationConfigs(mSlotEffects);

        IDataPropertyObject SlotData()
            => mSlotEffects.Count == 1 ? mSlotEffects[0].Properties : new MultipleDataPropertyObject(mSlotEffects.Select(effect => (IDataPropertyObject)effect.Properties).ToList());

        // 参数值 commit：数据对象不变，按当前值重算 config 并 keyed-diff 复用控件；自动化轨集合也随当前值涌现（条件轨显隐），故一并重建默认值行。
        // 重算 defer 到下一 UI 调度：commit 可能发生在控件自身事件回调链中（如 ComboBox 的 SelectionChanged），
        // 同步重算会重入修改控件集合（Avalonia ComboBox 在其 SelectionChanged 中 Clear/重填 Items 会抛异常）。
        // pending 标志合并一拍内的多次触发（多选扇出的逐对象 Modified 也经此归并）；dispose 后 pending 的回调命中 Reconcile 的空数据对象保护，安全空转。
        void ReconcileController()
        {
            if (mReconcilePending)
                return;
            mReconcilePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                mReconcilePending = false;
                mController?.Reconcile(SlotPropertyConfig());
                RebuildAutomationRows();
            });
        }

        // 该槽位的连续自动化轨默认值行（分段轨无默认基线、跳过）；混在参数控件后，按 AutomationKey.Effect 路由。
        // AutomationDefaultRow 自带多 part 语义（三态合并 + 扇出 + 共享撤销根），只递有实例的 part。
        void RebuildAutomationRows()
        {
            foreach (var row in mAutomationRows)
                row.Dispose();
            mAutomationRows.Clear();
            mAutomationContainer.Children.Clear();

            foreach (var kvp in SlotAutomationConfigs())
            {
                if (kvp.Value.IsPiecewise)
                    continue;

                var row = new AutomationDefaultRow(mSlotParts, AutomationKey.Effect(mIndex, kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id, kvp.Value);
                mAutomationRows.Add(row);
                mAutomationContainer.Children.Add(row);
            }
        }

        void RefreshAutomationRows()
        {
            foreach (var row in mAutomationRows)
                row.Refresh();
        }

        public void Dispose()
        {
            s.DisposeAll();
            foreach (var row in mAutomationRows)
                row.Dispose();
            mAutomationRows.Clear();
            mController?.ResetConfig();
        }

        readonly List<IMidiPart> mSlotParts = new();
        readonly List<IEffect> mSlotEffects = new();
        readonly int mIndex;
        readonly PropertyObjectController? mController;
        readonly StackPanel mAutomationContainer = new() { Orientation = Orientation.Vertical };
        readonly List<AutomationDefaultRow> mAutomationRows = new();
        readonly StackPanel mRoot;
        bool mReconcilePending = false;
        readonly DisposableManager s = new();
    }

    IReadOnlyList<IMidiPart> mParts = Array.Empty<IMidiPart>();
    Button? mAddButton = null;
    bool mBatching = false;
    readonly List<EffectView> mEffectViews = new();
    readonly DisposableManager s = new();
}
